# InjectFix实现原理

Created: Jan 6, 2021 3:25 PM
Tags: C#, Unity, 热更新

# 简介

InjectFix是腾讯开源的Unity C#热更新解决方案。本文主要介绍InjectFix的相关内容，从手把手的一个例子来介绍如何使用InjectFix，一直到阅读源码来分析它的内部实现原理。

项目主页：

[https://github.com/Tencent/InjectFix](https://github.com/Tencent/InjectFix)

原理介绍（原作者）：

[https://www.oschina.net/news/109803/injectfix-opensource](https://www.oschina.net/news/109803/injectfix-opensource)

# 如何使用InjectFix

这里我们会从一个空项目开始，介绍如何使用InjectFix。并根据这个例子做引子来进行它的原理分析。

本文的例子的源码都在：[https://github.com/sandin/InjectFixSample](https://github.com/sandin/InjectFixSample)

这里InjectFix的使用说明主要是参考Github上面的官方帮忙文档：

[https://github.com/Tencent/InjectFix/blob/master/Doc/quick_start.md](https://github.com/Tencent/InjectFix/blob/master/Doc/quick_start.md)

本例中使用的开发环境如下：

- macOS Big Sur 11.1
- Unity 2019.4.17f1c1 (安装目录：/Applications/Unity/Hub/Editor/2019.4.17f1c1/Unity.app）

## 接入InjectFix

第一步就是讲InjectFix的源码clone到本地：

```csharp
$ git clone git@github.com:Tencent/InjectFix.git
```

然后准备开始编译源码，windows环境的编译脚本为 build_for_unity.bat ，Mac环境为 build_for_unity.sh ，需要先修改该编译脚本的UNITY_HOME值，将其修改为本机Unity编辑器的安装目录。

例如本例中我们修改 build_for_unity.sh 中的
`UNITY_HOME="/Applications/Unity/Hub/Editor/2019.4.17f1c1/Unity.app"`

然后执行编译脚本即可开始编译。

```csharp
$ cd InjectFix/VsProj
$ ./build_for_unity.sh
```

这个编译脚本会使用Unity自带的Mono编译器，将源码中的一些CS脚本进行编译，并生成一些CS脚本，最后编译出IFix的核心库 `IFix.Core.dll` ，这个库就是唯一需要接入到项目中去的热更新库。

编译成功后会生成如下几个文件：

- Source/UnityProj/Assets/Plugins/IFix.Core.dll
- Source/UnityProj/IFixToolkit/IFix.exe
- Source/UnityProj/IFixToolkit/IFix.exe.mdb
- Source/VSProj/Instruction.cs
- Source/VSProj/ShuffleInstruction.exe

接下来我们创建一个新的项目，并将InjectFix的如下文件夹拷贝到我们的项目根目录。

- 项目根目录
    - IFixToolKit        ← InjectFix/Source/UnityProj/IFixToolKit
    - Assets
        - IFix            ← InjectFix/Source/UnityProj/Assets/IFix
        - Plugins     ← InjectFix/Source/UnityProj/Assets/Plugins

拷贝后则会发现Unity编辑器的菜单栏增加了 【InjectFix】菜单。

然后我们新建一个C#脚本文件，作为热更新的实验，代码如下：

```csharp
using System.Collections;
using System.Collections.Generic;
using System.IO;
using IFix;
using IFix.Core;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    void Start()
    {
        string text = Path.Combine(Application.streamingAssetsPath, "Assembly-CSharp.patch.bytes");
        bool flag = File.Exists(text);
        if (flag)
        {
            Debug.Log("Load HotFix, patchPath=" + text);
            PatchManager.Load(new FileStream(text, FileMode.Open), true);
        }
    }

    void Update()
    {
    }

    void OnGUI()
    {
        if (GUI.Button(new Rect((Screen.width - 200) / 2, 20, 200, 100), "Call  FuncA"))
        {
            Debug.Log("Button, Call FuncA, result=" + FuncA());
        }
    }

    public string FuncA()
    {
        return "Old";
    }
}
```

然后通过提供Config文件，告诉IFix我们可能需要热更新的类有哪些（必须放到Editor目录下）。

```csharp
using System;
using System.Collections.Generic;
using IFix;

[Configure]
public class InterpertConfig
{
    [IFix]
    static IEnumerable<Type> ToProcess
    {
        get
        {
            return new List<Type>() {
                typeof(NewBehaviourScript),
            };
        }
    }
}
```

正常运行程序，点击按钮，会看到控制台输出 `FuncA` 的返回值为字符串 `Old` .

Unity会将我们的C#代码编译成DLL文件，路径为：`<ProjectRoot>\Library\ScriptAssemblies\Assembly-CSharp.dll`。此时这个DLL文件是还未进行任何插桩修改的，也就是暂时还没有热更新能力的。

  

在正式打包之前需要运行编辑器菜单 【InjectFix】-【Inject】来对我们的DLL进行自动插桩。（注意编辑器需要处在非运行状态才可进行注入）。

运行这个菜单工具后，这时IFix会根据我们提供的Config文件去给这些注册的类里面的每个方法插桩，它会直接修改 `<ProjectRoot>\Library\ScriptAssemblies\Assembly-CSharp.dll` 这个文件，正常注入后即可得到一个拥有热更新能力的DLL文件。

 

## 生成补丁

在打包完成后，例如需要对某个函数进行热修复，那么我们需要来制作补丁。

例如我们如下函数进行修复，将FuncA的返回值从 "Old" 修改为 ”New“，那么需要将需要打补丁的函数打上 `[Patch]` 的注解来告诉IFix我们希望给该函数打补丁。

```csharp
public class NewBehaviourScript : MonoBehaviour
{
		[Patch]
    public string FuncA()
    {
        return "New";
    }
}
```

然后运行编辑器菜单 【InjectFix】-【Fix】来对生成补丁，生成的补丁会保存在项目根目录的，文件名为： `Assembly-CSharp.patch.bytes`, 这是一个二进制的il字节码。

将补丁文件移动到我们想要放置补丁的目录下，使用如下代码即可自动加载和应用这些补丁：

```csharp
string text = Path.Combine(Application.streamingAssetsPath, "Assembly-CSharp.patch.bytes");
bool flag = File.Exists(text);
if (flag)
{
	Debug.Log("Load HotFix, patchPath=" + text);
	PatchManager.Load(new FileStream(text, FileMode.Open), true);
}
```

为了在编辑器里面实验，这里我们需要把代码回滚一下，回复到补丁之前的版本来验证热更新是否有效，如下：

```csharp
public class NewBehaviourScript : MonoBehaviour
{
    public string FuncA()
    {
        return "Old";
    }
}
```

这时在编辑器里运行，我们会发现控制台输出 FuncA 函数的输出值为 `Old` 。

然后我们再次点击菜单 【InjectFix】- 【Inject】 来进行插桩，再次运行则会发现控制台的输出会变成 `New` ：

```csharp
Load HotFix, patchPath=/Users/liudingsan/project/unity/IFixTest/IFixTest/Assets/StreamingAssets/Assembly-CSharp.patch.bytes
Button, Call FuncA, result=New
```

这里我们就成功的使用InjectFix进行了C#代码的热更新。接下来我们会深入源码中来了解InjectFix的具体实现原理。

# 原理分析

IFix的原理主要包括两个部分：

1. 自动插桩，首先在代码里面插桩，进入这些的函数的时候判断是否需要热更新，如果需要则直接跳转去执行热更新补丁中的IL指令。
2. 生成补丁，将需要热更新的代码生成为IL指令。

技术难点在于去实现一个IL运行时的虚拟机，支持所有的IL指令。

## 自动插桩

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

   

       

## 补丁格式

参考源码：Source\VSProj\Src\Builder\FileVirtualMachineBuilder.cs

[补丁二进制文件格式](https://www.notion.so/e4866e537dbd461f9edae54fafbcfd5a)