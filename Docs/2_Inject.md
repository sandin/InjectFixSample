# InjectFix实现原理(二) - 自动插桩

​      

# 原理分析

IFix的原理主要包括两个部分：

1. 自动插桩，首先在代码里面插桩，进入这些的函数的时候判断是否需要热更新，如果需要则直接跳转去执行热更新补丁中的IL指令。
2. 生成补丁，将需要热更新的代码生成为IL指令。

技术难点在于去实现一个IL运行时的虚拟机，支持所有的IL指令。

​        

# 自动插桩

插桩的入口在菜单 【InjectFix】-【Inject】，源码在：Source/UnityProj/Assets/IFix/Editor/ILFixEditor.cs

```cpp
				[MenuItem("InjectFix/Inject", false, 1)]
        public static void InjectAssemblys()
        {
            if (EditorApplication.isCompiling || Application.isPlaying)
            {
                UnityEngine.Debug.LogError("compiling or playing");
                return;
            }
            EditorUtility.DisplayProgressBar("Inject", "injecting...", 0);
            try
            {
                InjectAllAssemblys();
            }
            catch(Exception e)
            {
                UnityEngine.Debug.LogError(e);
            }
            EditorUtility.ClearProgressBar();
				}
```

`InjectAllAssemblys` 对 `./Library/ScriptAssemblies` 目录下的两个dll文件进行注入：

- Assembly-CSharp.dll
- Assembly-CSharp-firstpass.dll

```csharp
				/// <summary>
        /// 对指定的程序集注入
        /// </summary>
        /// <param name="assembly">程序集路径</param>
        public static void InjectAssembly(string assembly)
        {
				}
```

反编译它可以看到它给原代码进行了插桩，修改如下：

```csharp
public class NewBehaviourScript2 : MonoBehaviour
{
    private void Start()
    {
        if (WrappersManagerImpl.IsPatched(16))
        {
            WrappersManagerImpl.GetPatch(16).__Gen_Wrap_0(this);
            return;
        }
        string text = Path.Combine(Application.streamingAssetsPath, "Assembly-CSharp.patch.bytes");
        bool flag = File.Exists(text);
        if (flag)
        {
            Debug.Log("Load HotFix, patchPath=" + text);
            PatchManager.Load(new FileStream(text, FileMode.Open), true);
        }
    }

    private void Update()
    {
        if (WrappersManagerImpl.IsPatched(17))
        {
            WrappersManagerImpl.GetPatch(17).__Gen_Wrap_0(this);
            return;
        }
    }

    private void OnGUI()
    {
        if (WrappersManagerImpl.IsPatched(18))
        {
            WrappersManagerImpl.GetPatch(18).__Gen_Wrap_0(this);
            return;
        }
        bool flag = GUI.Button(new Rect((float)((Screen.width - 200) / 2), 20f, 200f, 100f), "Call FuncA");
        if (flag)
        {
            Debug.Log("Button, Call FuncA, result=" + this.FuncA());
        }
    }

    public string FuncA()
    {
        if (WrappersManagerImpl.IsPatched(19))
        {
            return WrappersManagerImpl.GetPatch(19).__Gen_Wrap_5(this);
        }
        return "Old";
    }
}
```

可以看到每个函数都增加一个if判断的插桩，用来判断这个方法是否需要热更新的版本，如果有则直接跳转去执行热更新的代码，否则正常执行该方法的原代码。

```csharp
if (WrappersManagerImpl.IsPatched(19))
{
    return WrappersManagerImpl.GetPatch(19).__Gen_Wrap_5(this);
}
```

其中判断是否有patch以及获取patch都是由IFix生成的代码来实现的，如下：（生成这段代码的源码在：[https://github.com/Tencent/InjectFix/blob/master/Source/VSProj/Src/Tools/CodeTranslator.cs](https://github.com/Tencent/InjectFix/blob/master/Source/VSProj/Src/Tools/CodeTranslator.cs)）

```csharp
namespace IFix 
{
    public class WrappersManagerImpl : WrappersManager
    {
        public static bool IsPatched(int id)
        {
            return id < ILFixDynamicMethodWrapper.wrapperArray.Length && ILFixDynamicMethodWrapper.wrapperArray[id] != null;
        }

        public static ILFixDynamicMethodWrapper GetPatch(int id)
        {
            return ILFixDynamicMethodWrapper.wrapperArray[id];
        }
    }
}
```

调用patch的代码，实现如下：

```csharp
namespace IFix
{
    public class ILFixDynamicMethodWrapper
    {
        public string __Gen_Wrap_5(object P0)
        {
            Call call = Call.Begin();
            if (this.anonObj != null)
            {
                call.PushObject(this.anonObj);
            }
            call.PushObject(P0);
            this.virtualMachine.Execute(this.methodId, ref call, (this.anonObj != null) ? 2 : 1, 0);
            return call.GetAsType<string>(0);
        }

        private VirtualMachine virtualMachine;
    }
}
```

这里我们看到热更新的逻辑就是将参数入栈，然后调用IFix实现的il虚拟机( `VirtualMachine` ) 来执行这个函数。

这里的`VirtualMachine`是由接入项目中的 `Assets\Plugins\IFix.Core.dll` 提供的，源码在：[https://github.com/Tencent/InjectFix/blob/master/Source/VSProj/Src/Core/VirtualMachine.cs](https://github.com/Tencent/InjectFix/blob/master/Source/VSProj/Src/Core/VirtualMachine.cs)

这个`VirtualMachine`虚拟机是由加载补丁的时候 `PatchManager.Load`函数创建的:

```csharp
public static class PatchManager
{
	unsafe static public VirtualMachine Load(Stream stream, bool checkNew = true)
	{
		
		// ...
		// stream是二进制的补丁，里面放着热更新代码的IL指令，该二进制文件格式参考后面章节
		BinaryReader reader = new BinaryReader(stream);
		// 这里会将二进制的补丁文件的所有热更新的方法定义及IL指令都读出来
		// 并把所有指令都保存到unmanagedCodes变量中，传给 VirtualMachine 构造函数。
		unmanagedCodes = (Instruction**)nativePointer.ToPointer(); 
		
		var virtualMachine = new VirtualMachine(unmanagedCodes, () =>
                {
                    for (int i = 0; i < nativePointers.Count; i++)
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(nativePointers[i]);
                    }
                })
                {
                    ExternTypes = externTypes,
                    ExternMethods = externMethods,
                    ExceptionHandlers = exceptionHandlers.ToArray(),
                    InternStrings = internStrings,
                    FieldInfos = fieldInfos,
                    AnonymousStoreyInfos = anonymousStoreyInfos,
                    StaticFieldTypes = staticFieldTypes,
                    Cctors = cctors
                };
		// ...
	}
}
```

创建虚拟机方法如下：

```csharp
internal VirtualMachine(Instruction** unmanaged_codes, Action on_dispose);
```

- 参数1： 热修复的所有函数及其IL指令。
- 参数2：当虚拟机被消耗时，用于释放相关内存的析构函数。

执行热更新的代码，主要通过调用 `VirtualMachine` 的 `Execute` 函数来实现的，这个方法会直接去执行热更新补丁中这个函数的IL指令：

```csharp
public void Execute(int methodIndex, ref Call call, int argsCount, int refCount = 0)
{
	Execute(unmanagedCodes[methodIndex], call.argumentBase + refCount, call.managedStack,
                call.evaluationStackBase, argsCount, methodIndex, refCount, call.topWriteBack);
}

public Value* Execute(Instruction* pc, Value* argumentBase, object[] managedStack,
            Value* evaluationStackBase, int argsCount, int methodIndex,
            int refCount = 0, Value** topWriteBack = null)
{
	// ...
}
```

这里传参的pc就直接是热更新代码的IL指令，关于IL的说明可查看wiki：[https://en.wikipedia.org/wiki/Common_Intermediate_Language](https://en.wikipedia.org/wiki/Common_Intermediate_Language)
