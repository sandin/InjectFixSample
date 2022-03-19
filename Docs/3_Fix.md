# InjectFix实现原理(三) - 生成补丁

在本节中，我们主要跟着例子去阅读iFix的源码，看看iFix是如何去生成二进制的补丁文件，其中包括了所有热更新所需的函数及其IL指令。

它的主要原理是用Mono.cecil这个库去反编译出DLL里面的代码（IL指令），然后去找到标记为需要热更新的函数，并将这些函数里面的代码都转成iFix自己实现的虚拟机支持的IL指令，然后将IL指令都保存成二进制的热更新补丁文件。

​            

## 生成补丁

### GenPatch

点击菜单【InjectFix】-【Fix】来生成补丁，源码在：`Assets/IFix/Editor/ILFixEditor.cs`

```csharp
        [MenuItem("InjectFix/Fix", false, 2)]
        public static void Patch()
        {
            EditorUtility.DisplayProgressBar("Generate Patch for Edtior", "patching...", 0);
            try
            {
                foreach (var assembly in injectAssemblys)
                {
                    var assembly_path = string.Format("./Library/{0}/{1}.dll", GetScriptAssembliesFolder(), assembly);
                    GenPatch(assembly, assembly_path, "./Assets/Plugins/IFix.Core.dll",
                        string.Format("{0}.patch.bytes", assembly));
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
            }
            EditorUtility.ClearProgressBar();
        }
```

这里和 `Inject` 菜单命令一样，会去遍历两个dll，然后调用 `GenPatch` 方法来生成补丁文件：

* Assembly-CSharp.dll

* Assembly-CSharp-firstpass.dll

   

GenPatch函数生成patch的逻辑如下：

```csharp
        /// <summary>
        /// 生成patch
        /// </summary>
        /// <param name="assembly">程序集名，用来过滤配置</param>
        /// <param name="assemblyCSharpPath">程序集路径</param>
        /// <param name="corePath">IFix.Core.dll所在路径</param>
        /// <param name="patchPath">生成的patch的保存路径</param>
        public static void GenPatch(string assembly, string assemblyCSharpPath
            = "./Library/ScriptAssemblies/Assembly-CSharp.dll", 
            string corePath = "./Assets/Plugins/IFix.Core.dll", string patchPath = "Assembly-CSharp.patch.bytes")
        {
            // 查找需要生成补丁的函数列表（即打了[Patch]注解的函数）
            var patchMethods = Configure.GetTagMethods(typeof(PatchAttribute), assembly).ToList();
          	// 不支持泛型的方法
            var genericMethod = patchMethods.FirstOrDefault(m => hasGenericParameter(m));
            if (genericMethod != null)
            {
                throw new InvalidDataException("not support generic method: " + genericMethod);
            }

            if (patchMethods.Count == 0)
            {
                return;
            }

            // 查找需要新增的函数列表（即打了[Interpret]注解的函数或类）
            var newMethods = Configure.GetTagMethods(typeof(InterpretAttribute), assembly).ToList();
            var newClasses = Configure.GetTagClasses(typeof(InterpretAttribute), assembly).ToList();
            genericMethod = newMethods.FirstOrDefault(m => hasGenericParameter(m));
            if (genericMethod != null)
            {
                throw new InvalidDataException("not support generic method: " + genericMethod);
            }

            // 将找到的这些需要打补丁或需要新增的函数和类写入到cfg文件中
            var processCfgPath = "./process_cfg";

            using (BinaryWriter writer = new BinaryWriter(new FileStream(processCfgPath, FileMode.Create,
                FileAccess.Write)))
            {
                writeMethods(writer, patchMethods);
                writeMethods(writer, newMethods);
                writeClasses(writer, newClasses);
            }

            List<string> args = new List<string>() { "-patch", corePath, assemblyCSharpPath, "null",
                processCfgPath, patchPath };

            foreach (var path in
                (from asm in AppDomain.CurrentDomain.GetAssemblies()
                    select Path.GetDirectoryName(asm.ManifestModule.FullyQualifiedName)).Distinct())
            {
                try
                {
                    //UnityEngine.Debug.Log("searchPath:" + path);
                    args.Add(path);
                }
                catch { }
            }

            // 然后调用 IFix.exe来生成补丁文件
            CallIFix(args);

            File.Delete(processCfgPath);

            AssetDatabase.Refresh();
        }
```

这个函数主要是查找DLL中哪些函数是需要打补丁的，即拥有IFix的注解：

* `[IFix.patch]` ： 需要打补丁的方法。
* `[IFix.Interpret]` : 需要新增的属性，方法或类。

然后将这些需要打入到补丁的函数列表写入到一个名为 `process_cfg` 的临时配置文件，作为参数传给 `IFixToolKit/IFix.exe` 进行生成补丁。

​     

### IFix.exe

`IFixToolKit/IFix.exe` 程序是运行 `build_for_unity.sh` 脚本后编译生成的。

从编译脚本来看，这个exe的源码主要在 InjectFix/Src/Tools 目录下的CS文件，以及动态生成出来的 `Instruction.cs` 代码文件。并且依赖了Mono.Cecil.dll库。Mono.Cecil库是Mono的一个开源库，用来读取DLL文件，查找所有的类，并且可以修改它们，再将其保存到新的DLL文件。详情可参考项目主页：https://www.mono-project.com/docs/tools+libraries/libraries/Mono.Cecil/。

IFix.exe 这个工具的使用说明为：

* IFix -**inject** core_assmbly_path assmbly_path config_path patch_file_output_path injected_assmbly_output_path [search_path1, search_path2 ...]
* IFix -**inherit_inject** core_assmbly_path assmbly_path config_path patch_file_output_path injected_assmbly_output_path inherit_assmbly_path
* IFix -**patch** core_assmbly_path assmbly_path injected_assmbly_path config_path patch_file_output_path [search_path1, search_path2 ...]

该工具支持 Inject注入 和 生成Patch 两个功能，例如这里我们用来生成Patch的实际运行的命令及传参如下：

```bash
/Applications/Unity/Hub/Editor/2019.4.17f1c1/Unity.app/Contents/Managed/UnityEngine/../../MonoBleedingEdge/bin/mono --debug  --runtime=v4.0.30319 ./IFixToolKit/IFix.exe

# arg[0]: command 命令
-patch   

# arg[1]: core_assmbly_path IFix核心DLL库
./Assets/Plugins/IFix.Core.dl

# arg[2]: injected_assmbly_path 被注入的DLL库
./Library/ScriptAssemblies/Assembly-CSharp.dll
null 

# arg[3]: config_path 配置文件（生成的）
./process_cfg

# arg[4]: patch_file_output_path 补丁的输出路径
Assembly-CSharp.patch.bytes

# search_path 搜索目录
/Applications/Unity/Hub/Editor/2019.4.17f1c1/Unity.app/Contents/MonoBleedingEdge/lib/mono/unityjit
/Applications/Unity/Hub/Editor/2019.4.17f1c1/Unity.app/Contents/Managed/UnityEngine
/Applications/Unity/Hub/Editor/2019.4.17f1c1/Unity.app/Contents/Managed
/Applications/Unity/Hub/Editor/2019.4.17f1c1/Unity.app/Contents/UnityExtensions/Unity/UnityVR/Editor
/Applications/Unity/Hub/Editor/2019.4.17f1c1/PlaybackEngines/WindowsStandaloneSupport
/Applications/Unity/Hub/Editor/2019.4.17f1c1/Unity.app/Contents/PlaybackEngines/MacStandaloneSupport
/Applications/Unity/Hub/Editor/2019.4.17f1c1/PlaybackEngines/AndroidPlayer
/Applications/Unity/Hub/Editor/2019.4.17f1c1/PlaybackEngines/iOSSupport
/Applications/Visual Studio.app/Contents/Resources/lib/monodeve…
```

IFix.exe 这个工具的入口在 `InjectFix/Src/Tools/CSFix.cs` 源码中的 `Main` 函数。

```csharp
static void Main(string[] args)
{
    CodeTranslator tranlater = new CodeTranslator();
    AssemblyDefinition assembly = null;
  
    //尝试读取符号
    assembly = AssemblyDefinition.ReadAssembly(assmeblyPath,
        new ReaderParameters { ReadSymbols = true });
  
    var resolver = assembly.MainModule.AssemblyResolver as BaseAssemblyResolver;
    resolver.AddSearchDirectory(path);
  
    ilfixAassembly = AssemblyDefinition.ReadAssembly(args[1]);
  
    if (mode == ProcessMode.Inject)
    {
        // 注入逻辑
        tranlater.Process(assembly, ilfixAassembly, configure, mode);
    } 
    else
    {
        // 补丁生成流程
        configure = new PatchGenerateConfigure(assembly, args[4] /* patch_file_output_path */);
        tranlater.Process(assembly, ilfixAassembly, configure, mode);
      
        tranlater.Serialize(args[5] /* patch_file_output_path */);
    }
}
```

这里 `AssemblyDefinition` 是`Mono.Cecil` 库提供的，用来读取一个Assembly（DLL文件），并且先会尝试带符号的去读取，如果没有符号再使用无符号的方式读取DLL文件，最后交给 `CodeTranslator` 类的 `Process` 方法来进行注入或者打补丁的操作。

`Source/VSProj/Src/Tools/CodeTranslator.cs`

```csharp
	public ProcessResult Process(AssemblyDefinition assembly, AssemblyDefinition ilfixAassembly,
            GenerateConfigure configure, ProcessMode mode)
	{
    	// ...
    
    	init(assembly, ilfixAassembly);
        
        emitWrapperManager();
        
        // 从DLL中查找到所有类，除了IFix命名空间下的，泛型的，编译器生成的，以及补丁新增的类
        var allTypes = (from type in assembly.GetAllType()
                            where type.Namespace != "IFix" && !type.IsGeneric() && !(isCompilerGenerated(type) || isNewClass(type))
                            select type);
        
        // 遍历所有类的所有方法，除了构造器，编译器生成的，有泛型，以及返回值的类型带IsRequired修饰符的？
            foreach (var method in (
                from type in allTypes
                where !(isCompilerGenerated(type) || isNewClass(type)) && !type.HasGenericParameters
                from method in type.Methods
                where !method.IsConstructor && !isCompilerGenerated(method) && !method.HasGenericParameters && !method.ReturnType.IsRequiredModifier
                select method))
            {
                int flag;
                // [IFix.Interpret] 标记的新增的函数
                if (configure.TryGetConfigure("IFix.InterpretAttribute", method, out flag))
                {
                    methodToInjectType[method] = InjectType.Redirect;
                    hasRedirect = true;
                }
                // Configure文件，标记[IFix.IFix]的函数，用来返回可能打补丁的函数列表 
                else if(configure.TryGetConfigure("IFix.IFixAttribute", method, out flag))
                {
                    methodToInjectType[method] = InjectType.Switch;
                }
            }

            // 处理要生成补丁的函数
            foreach(var kv in methodToInjectType)
            {
                processMethod(kv.Key);
            }

            genCodeForCustomBridge();

            emitCCtor();

            postProcessInterfaceBridge();

            if (mode == ProcessMode.Inject)
            {
                redirectFieldRename();
                if (awaitUnsafeOnCompletedMethods.Count != 0)
                {
                    EmitRefAwaitUnsafeOnCompletedMethod();
                }
            } 

            return ProcessResult.OK;
	}	

	void init(AssemblyDefinition assembly, AssemblyDefinition ilfixAassembly)
	{
            this.assembly = assembly;
            // System.Object类及其公共方法	
            objType = assembly.MainModule.TypeSystem.Object;
            List<string> supportedMethods = new List<string>() { "Equals", "Finalize","GetHashCode", "ToString"};
            ObjectVirtualMethodDefinitionList = (from method in objType.Resolve().Methods where method.IsVirtual && supportedMethods.Contains(method.Name) select method).ToList();
            if (ObjectVirtualMethodDefinitionList.Count != 4)
            {
                throw new InvalidProgramException(); // Object的公共方法不包括这4个，视为非法的程序集
            }
            ObjectVirtualMethodDefinitionList.OrderBy(t => t.FullName);
            for (int methodIdx = 0; methodIdx < ObjectVirtualMethodDefinitionList.Count; methodIdx++)
            {
                virtualMethodToIndex.Add(ObjectVirtualMethodDefinitionList[methodIdx], methodIdx);
            }
            // System.Void类
            voidType = assembly.MainModule.TypeSystem.Void;

            // 新增一个类的定义，名为：ILFixDynamicMethodWrapper
            wrapperType = new TypeDefinition("IFix", DYNAMICWRAPPER /* ILFixDynamicMethodWrapper */, Mono.Cecil.TypeAttributes.Class
                | Mono.Cecil.TypeAttributes.Public, objType);
            assembly.MainModule.Types.Add(wrapperType);

            // 从IFix.Core.dll中找到VirtualMachine类和WrappersManager类
            TypeDefinition VirtualMachine;
            VirtualMachine = ilfixAassembly.MainModule.Types.Single(t => t.Name == "VirtualMachine");
            VirtualMachineType = assembly.MainModule.ImportReference(VirtualMachine);
            WrappersManagerType = assembly.MainModule.ImportReference(
                ilfixAassembly.MainModule.Types.Single(t => t.Name == "WrappersManager"));

            // 给 ILFixDynamicMethodWrapper 添加一些成员成员：
		    // private VirtualMachine virtualMachine;
		    // private int methodId;
		    // private object anonObj;
		    // public static ILFixDynamicMethodWrapper[] wrapperArray = new ILFixDynamicMethodWrapper[0];
            virualMachineFieldOfWrapper = new FieldDefinition("virtualMachine", Mono.Cecil.FieldAttributes.Private,
                    VirtualMachineType);
            wrapperType.Fields.Add(virualMachineFieldOfWrapper);
            methodIdFieldOfWrapper = new FieldDefinition("methodId", Mono.Cecil.FieldAttributes.Private,
                    assembly.MainModule.TypeSystem.Int32);
            wrapperType.Fields.Add(methodIdFieldOfWrapper);
            anonObjOfWrapper = new FieldDefinition("anonObj", Mono.Cecil.FieldAttributes.Private,
                    objType);
            wrapperType.Fields.Add(anonObjOfWrapper);
            wrapperArray = new FieldDefinition("wrapperArray", Mono.Cecil.FieldAttributes.Public
                | Mono.Cecil.FieldAttributes.Static,
                new ArrayType(wrapperType));
            wrapperType.Fields.Add(wrapperArray);

            idTagCtor_Ref = assembly.MainModule.ImportReference(
                ilfixAassembly.MainModule.Types.Single(t => t.Name == "IDTagAttribute")
                .Methods.Single(m => m.Name == ".ctor" && m.Parameters.Count == 1));

            // 给 ILFixDynamicMethodWrapper 添加构造器：
            // public ILFixDynamicMethodWrapper(VirtualMachine virtualMachine, int methodId, object anonObj)
        	// {
			//     this.virtualMachine = virtualMachine;
			//     this.methodId = methodId;
			//     this.anonObj = anonObj;
		    // }
            var objEmptyConstructor = assembly.MainModule.ImportReference(objType.Resolve().Methods.
                Single(m => m.Name == ".ctor" && m.Parameters.Count == 0));
            var methodAttributes = MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName;
            ctorOfWrapper = new MethodDefinition(".ctor", methodAttributes, voidType);
            ctorOfWrapper.Parameters.Add(new ParameterDefinition("virtualMachine",
                Mono.Cecil.ParameterAttributes.None, VirtualMachineType));
            ctorOfWrapper.Parameters.Add(new ParameterDefinition("methodId",
                Mono.Cecil.ParameterAttributes.None, assembly.MainModule.TypeSystem.Int32));
            ctorOfWrapper.Parameters.Add(new ParameterDefinition("anonObj",
                Mono.Cecil.ParameterAttributes.None, objType));
            var instructions = ctorOfWrapper.Body.Instructions;
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); // 将索引为 0 的参数(this)加载到计算堆栈上。
            instructions.Add(Instruction.Create(OpCodes.Call, objEmptyConstructor)); // 调用由传递的方法说明符指示的方法。
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); // 将索引为 0 的参数(this)加载到计算堆栈上。
            instructions.Add(Instruction.Create(OpCodes.Ldarg_1)); // 将索引为 1 的参数(virtualMachine)加载到计算堆栈上。
            instructions.Add(Instruction.Create(OpCodes.Stfld, virualMachineFieldOfWrapper)); // 用新值替换在对象引用或指针的字段中存储的值。
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); // this.methodId = methodId
            instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
            instructions.Add(Instruction.Create(OpCodes.Stfld, methodIdFieldOfWrapper));
            instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); // this.anonObj = anonObj
            instructions.Add(Instruction.Create(OpCodes.Ldarg_3));
            instructions.Add(Instruction.Create(OpCodes.Stfld, anonObjOfWrapper));
            instructions.Add(Instruction.Create(OpCodes.Ret)); // 从当前方法返回，并将返回值（如果存在）从调用方的计算堆栈推送到被调用方的计算堆栈上。
            wrapperType.Methods.Add(ctorOfWrapper);

            //begin init itfBridgeType
            // public class ILFixInterfaceBridge : AnonymousStorey, IDisposable, IEnumerator, IEnumerator<object>
            bridgeMethodId = 0;
            var anonymousStoreyType = ilfixAassembly.MainModule.Types.Single(t => t.Name == "AnonymousStorey");
            anonymousStoreyTypeRef = assembly.MainModule.ImportReference(anonymousStoreyType);
            anonymousStoreyCtorRef = assembly.MainModule.ImportReference(
                anonymousStoreyType.Methods.Single(m => m.Name == ".ctor" && m.Parameters.Count == 5));

            objectVirtualMethodReferenceList = anonymousStoreyType.Methods.Where(m => m.Name.StartsWith("Object")).
                Select(m => assembly.MainModule.ImportReference(m)).ToList();

            itfBridgeType = new TypeDefinition("IFix", INTERFACEBRIDGE, TypeAttributes.Class | TypeAttributes.Public,
                    anonymousStoreyTypeRef);
            virualMachineFieldOfBridge = assembly.MainModule.ImportReference(anonymousStoreyType.Fields.Single(f => f.Name == "virtualMachine"));
            assembly.MainModule.Types.Add(itfBridgeType);
            addExternType(itfBridgeType);

            //end init itfBridgeType

            //begin init idMapper
            enumType = assembly.MainModule.ImportReference(typeof(System.Enum));
            idMapList = new List<TypeDefinition>();
            idMapType = null;
            //end init idMapper

            wrapperMethods = new List<MethodDefinition>();

            TypeDefinition Call;
            Call = ilfixAassembly.MainModule.Types.Single(t => t.Name == "Call");
            Call_Ref = assembly.MainModule.ImportReference(Call);
            Call_Begin_Ref = importMethodReference(Call, "Begin");
            Call_PushRef_Ref = importMethodReference(Call, "PushRef");
            Call_PushValueType_Ref = importMethodReference(Call, "PushValueType");
            Call_GetAsType_Ref = importMethodReference(Call, "GetAsType");

            VirtualMachine_Execute_Ref = assembly.MainModule.ImportReference(
                VirtualMachine.Methods.Single(m => m.Name == "Execute" && m.Parameters.Count == 4));

            Utils_TryAdapterToDelegate_Ref = assembly.MainModule.ImportReference(
                ilfixAassembly.MainModule.Types.Single(t => t.FullName == "IFix.Core.Utils")
                .Methods.Single(m => m.Name == "TryAdapterToDelegate"));

        	// 基础类型的IL指令对照表
            ldinds = new Dictionary<TypeReference, OpCode>()
            {
                {assembly.MainModule.TypeSystem.Boolean, OpCodes.Ldind_U1 },
                {assembly.MainModule.TypeSystem.Byte, OpCodes.Ldind_U1 },
                {assembly.MainModule.TypeSystem.SByte, OpCodes.Ldind_I1 },
                {assembly.MainModule.TypeSystem.Int16, OpCodes.Ldind_I2 },
                {assembly.MainModule.TypeSystem.Char, OpCodes.Ldind_U2 },
                {assembly.MainModule.TypeSystem.UInt16, OpCodes.Ldind_U2 },
                {assembly.MainModule.TypeSystem.Int32, OpCodes.Ldind_I4 },
                {assembly.MainModule.TypeSystem.UInt32, OpCodes.Ldind_U4 },
                {assembly.MainModule.TypeSystem.Int64, OpCodes.Ldind_I8 },
                {assembly.MainModule.TypeSystem.UInt64, OpCodes.Ldind_I8 },
                {assembly.MainModule.TypeSystem.Single, OpCodes.Ldind_R4 },
                {assembly.MainModule.TypeSystem.Double, OpCodes.Ldind_R8 },
                {assembly.MainModule.TypeSystem.IntPtr, OpCodes.Ldind_I },
                {assembly.MainModule.TypeSystem.UIntPtr, OpCodes.Ldind_I },
            };

            stinds = new Dictionary<TypeReference, OpCode>()
            {
                {assembly.MainModule.TypeSystem.Boolean, OpCodes.Stind_I1 },
                {assembly.MainModule.TypeSystem.Byte, OpCodes.Stind_I1 },
                {assembly.MainModule.TypeSystem.SByte, OpCodes.Stind_I1 },
                {assembly.MainModule.TypeSystem.Int16, OpCodes.Stind_I2 },
                {assembly.MainModule.TypeSystem.Char, OpCodes.Stind_I2 },
                {assembly.MainModule.TypeSystem.UInt16, OpCodes.Stind_I2 },
                {assembly.MainModule.TypeSystem.Int32, OpCodes.Stind_I4 },
                {assembly.MainModule.TypeSystem.UInt32, OpCodes.Stind_I4 },
                {assembly.MainModule.TypeSystem.Int64, OpCodes.Stind_I8 },
                {assembly.MainModule.TypeSystem.UInt64, OpCodes.Stind_I8 },
                {assembly.MainModule.TypeSystem.Single, OpCodes.Stind_R4 },
                {assembly.MainModule.TypeSystem.Double, OpCodes.Stind_R8 },
                {assembly.MainModule.TypeSystem.IntPtr, OpCodes.Stind_I },
                {assembly.MainModule.TypeSystem.UIntPtr, OpCodes.Stind_I },
            };

            initStackOp(Call, assembly.MainModule.TypeSystem.Boolean);
            initStackOp(Call, assembly.MainModule.TypeSystem.Byte);
            initStackOp(Call, assembly.MainModule.TypeSystem.SByte);
            initStackOp(Call, assembly.MainModule.TypeSystem.Int16);
            initStackOp(Call, assembly.MainModule.TypeSystem.UInt16);
            initStackOp(Call, assembly.MainModule.TypeSystem.Char);
            initStackOp(Call, assembly.MainModule.TypeSystem.Int32);
            initStackOp(Call, assembly.MainModule.TypeSystem.UInt32);
            initStackOp(Call, assembly.MainModule.TypeSystem.Int64);
            initStackOp(Call, assembly.MainModule.TypeSystem.UInt64);
            initStackOp(Call, assembly.MainModule.TypeSystem.Single);
            initStackOp(Call, assembly.MainModule.TypeSystem.Double);
            initStackOp(Call, assembly.MainModule.TypeSystem.Object);
            initStackOp(Call, assembly.MainModule.TypeSystem.IntPtr);
            initStackOp(Call, assembly.MainModule.TypeSystem.UIntPtr);
        
}
```

​     

emitWrapperManager() 函数生成 WrapperManager类：

```csharp
class WrapperManagerImpl {
    
    private VirtualMachine _virtualMachine;
    
    public WrapperManagerImpl(VirtualMachine vm) {
        super();
        this._virtualMachine = vm;
    }
    
    public static void GetPatch(int id) {
        return this.wrapperArray[id];
    }
    
    public static boolean IsPatched(int id) {
        if (this.wrapperArray.length > 0) {
            return this.wrapperArray[id] != null;
        }
    }
}
```

​        

处理需要打补丁的函数：

```csharp
void processMethod(MethodDefinition method)
{
    getMethodId(method, null,true);
}

/// <summary>
/// 获取一个函数的id
/// 该函数会触发指令序列生成
/// </summary>
/// <param name="callee">被调用函数</param>
/// <param name="caller">调用者</param>
/// <param name="directCallVirtual">是个虚函数，会生成指令序列，
/// 但是调用通过反射来调用</param>
/// <param name="callerInjectType">调用者的注入类型</param>
/// <returns>负数表示需要反射访问原生，0或正数是指令数组下标</returns>
// #lizard forgives
unsafe MethodIdInfo getMethodId(MethodReference callee, MethodDefinition caller, bool isCallvirt,
    bool directCallVirtual = false, InjectType callerInjectType = InjectType.Switch)
{
	// ...
}
```

`getMethodId` 这个函数很长，逻辑比较复杂，下面我们主要以文字或伪代码的形式来说明它的具体逻辑：

首先我们需要了解到所有的方法分为如下类型:

* Extern, 外部方法（原生方法，如果是dll之外的方法，或者是构造函数，析构函数，作为虚拟机之外（extern）的方法）
* InteralVirtual, 内部虚方法
* Internal, 内部方法
* Invalid, 不支持的方法，例如含泛型方法

​      

然后在处理每一个method的，如果它不是虚函数，或者是外部函数（这里的外部是相对于IFix的虚拟机而已的），则调用 `addExternMethod` 来添加一个原生方法的引用，返回的MethodIdInfo {Type = Extern}

```c#
int addExternMethod(MethodReference callee, MethodDefinition caller)
{
   // 添加方法泛型的Type
    addExternType(typeArg);
    
    // 添加方法返回值的Type
    addExternType(callee.ReturnType);
    
    // 添加方法的类的Type
    addExternType(callee.DeclaringType)
    
    // 添加方法参数的Type
    addExternType(p.ParameterType);
    
    // 最后将其保存起来先
    externMethodToId[callee] = methodId;
    externMethods.Add(callee);
}
```

并且在依次弄完了各种Type之后，就开始处理method本身，将method转成iFix虚拟机支持的IL指令。

每个method都拥有一系列的Instruction指令集合，一般来说一个method的第一个指令如下：

| Code       | Operand          | Note                                       |
| ---------- | ---------------- | ------------------------------------------ |
| StackSpace | local \|maxstack | 该函数的局部变量个数 << 16 \| 最大堆栈大小 |

每个函数第一句都是栈的信息，包括栈上局部变量的个数，栈的大小，这是因为C#虚拟机是基于栈的。

接下面的Instruction指令，就是处理栈上的局部变量的。然后处理函数的异常Exception。

因为这里都是根据Mono.Cecil库解析出来的一个C#函数的所有信息：

```C#
namespace Mono.Cecil.Cil {

	public sealed class MethodBody {
        internal int max_stack_size; // 最大栈大小
		internal int code_size;  // IL指令个数
		internal bool init_locals; // 局部变量个数
        
        internal Collection<Instruction> instructions;  // IL指令
		internal Collection<ExceptionHandler> exceptions; // 异常
		internal Collection<VariableDefinition> variables; // 局部变量
        
    }
}
```

具体可以参考源码：https://github.com/mono/cecil/blob/main/Mono.Cecil.Cil/MethodBody.cs

​         

最后进入正题，就是函数的代码指令集合，`method.Body.Instructions`. 逐条的循环处理每一条IL指令：

处理所有的IL指令的逻辑比较枯燥，大部分情况下都是原样的保存原始的指令，除了几个需要特别处理的IL指令，例如函数调用这种，就需要判断到底是应该跳到哪个函数去执行，因为可能需要调到外部虚拟机的原生代码去执行，又或者需要调到iFix内部的虚拟机的热更新代码。

其实在补丁里面每个函数内的IL指令和正常的一个C#函数的IL指令是差不多的，例如一个C#的函数如下:

```c#
    public String test(int arg) {
        A obj;
    	String a = "hello world";
        int b = 1;
        Console.WriteLine(b);
        obj = new A();
        Console.WriteLine(obj);
        return a;
    }
```

使用 [Ildasmexe](https://docs.microsoft.com/en-us/dotnet/framework/tools/ildasm-exe-il-disassembler) 工具反编译其IL指令如下：

```
.method public instance hidebysig cil managed ofname test
	return: (string)
	params: (int32 arg)
{
	// Code size (0x1C)
	.locals init ([0] Program/A, [1] string, [2] int32)
	.maxstack 1
	IL_0000: ldstr "hello world"
	IL_0005: stloc.1
	IL_0006: ldc.i4.1
	IL_0007: stloc.2
	IL_0008: ldloc.2
	IL_0009: call System.Void System.Console::WriteLine(System.Int32)
	IL_000e: newobj System.Void Program/A::.ctor()
	IL_0013: stloc.0
	IL_0014: ldloc.0
	IL_0015: call System.Void System.Console::WriteLine(System.Object)
	IL_001a: ldloc.1
	IL_001b: ret
}
```

一个函数的IL指令也是和Mono.Cecil库解析出来的MethodBody是对应的：

* locals 局部变量
* maxstack 栈大小
* IL指令

​     

### 序列化补丁到二进制文件

最后处理完所有的函数之后，将所有的函数和指令写入到二进制patch文件中。

#### 序列化外部函数

外部函数在patch二进制文件的结构体如下：

```c#
    struct IFixExternMethod
    {
        bool isGenericInstance;   // MethodReference.IsGenericInstance
        string declaringType;     // MethodReference.DeclaringType
        string methodName;        // MethodReference.Name
        string[] genericArgs;     // GenericInstanceMethod.GenericArguments
        IFIxParameter[] parameters;
    }

    struct IFIxParameter
    {
        bool isGeneric;
        string declaringType;
    }
```



这里我们详细的阅读源码看看这些字段具体是存的什么：

首先看函数是不是泛型实例函数 (`isGenericInstance`)，例如泛型函数为：`T Call<T>()` ，那么在真正调用这个函数时，可能引用的函数时该泛型函数的实例：例如 `object Call<object>()` ，其中泛型 `T` 实例为 `object` 类型。

所有函数都会先保存两个字段： 该函数所属的类型 `declaringType`, 该函数的函数名 `methodName`.

如果是泛型函数的实例，主要需要记录的就是泛型的类型列表 `genericArgs`, 保存每一个泛型参数的类型。

然后就是保存函数的参数列表 `parameters`，这里复杂的也是处理泛型，咱们举个例子：

```c#
class SampleClass<T>
{
    void Swap<U>(T t, U u, int i) { }
}
```

对于这个函数来说，有3个参数:

* `t`, 类型是泛型 `T`, 这个泛型来自于 `SampleClass`类型( `GenericParameterType.Type`) 。
* `u`, 类型是泛型 `U`, 这个泛型来自于 `Swap`函数( `GenericParameterType.Method`) 。
* `i`, 类型是int，它不是一个泛型参数。



```c#
    void writeMethod(BinaryWriter writer, MethodReference method)
    {
        writer.Write(method.IsGenericInstance);
        if (method.IsGenericInstance)   // 如果该函数是泛型的实例 （函数本身是不是泛型函数，而不是指参数有没有泛型）
        {
            //Console.WriteLine("GenericInstance:" + externMethod);
            writer.Write(externTypeToId[method.DeclaringType]);  // 函数所在类型
            writer.Write(method.Name);                           // 函数名
            // method.IsGenericInstance == true时，MethodReference可向下转型为GenericInstanceMethod泛型实例函数
            var typeArgs = ((GenericInstanceMethod)method).GenericArguments; // 泛型参数
            writer.Write(typeArgs.Count);                                    // 泛型参数的个数
            for (int typeArg = 0;typeArg < typeArgs.Count;typeArg++)
            {
                if (isCompilerGenerated(typeArgs[typeArg])) // 如果是编译器生成的类型，则使用bridgeType
                {
                    typeArgs[typeArg] = itfBridgeType;
                }
                writer.Write(externTypeToId[typeArgs[typeArg]]); // 保存泛型参数的类型
            }
            
            writer.Write(method.Parameters.Count);
            foreach (var p in method.Parameters)
            {
                // 判断一个类型的泛型实参是否有来自函数的泛型实参
                // 对于ParameterType而言，如果它是GenericParameter子类型，则其Type字段中记录了其泛型是来自于类型还是函数
                bool paramIsGeneric = p.ParameterType.HasGenericArgumentFromMethod(); 
                writer.Write(paramIsGeneric);  // 记录该参数为泛型
                if (paramIsGeneric) // 如果参数的泛型是来自于函数本身
                {
                    if (p.ParameterType.IsGenericParameter)
                    {
                        writer.Write(p.ParameterType.Name);   // 是泛型，就存泛型参数的名称，它其实是对于类型泛型的引用，即类型泛型列表中的index, 格式为： `!!<index>`，例如: `!!0`, `!!1`, `!!2`, 其中index是method.GetGenericArguments()列表中的index
                    }
                    else
                    {
                        writer.Write(p.ParameterType.GetAssemblyQualifiedName(method.DeclaringType, true)); // 不是泛型，就存正常的类型，例如 `System.object`
                    }
                }
                else    // 如果参数的泛型来自于外层的类型
                {
                    if (p.ParameterType.IsGenericParameter)
                    {
                        writer.Write(externTypeToId[(p.ParameterType as GenericParameter)
                            .ResolveGenericArgument(method.DeclaringType)]);  // 在方法定义的类型中查找泛型参数对应的的实参
                    }
                    else
                    {
                        writer.Write(externTypeToId[p.ParameterType]);   // 直接用参数的类型即可
                    }
                }
            }
        }
        else // 不是泛型函数
        {
            //Console.WriteLine("not GenericInstance:" + externMethod);
            if (!externTypeToId.ContainsKey(method.DeclaringType))
            {
                throw new Exception("externTypeToId do not exist key: " + method.DeclaringType
                    + ", while process method: " + method);
            }
            writer.Write(externTypeToId[method.DeclaringType]);
            writer.Write(method.Name);
            writer.Write(method.Parameters.Count);
            foreach (var p in method.Parameters)
            {
                var paramType = p.ParameterType;
                if (paramType.IsGenericParameter) // 是泛型参数
                {
                    paramType = (paramType as GenericParameter).ResolveGenericArgument(method.DeclaringType); // 查找泛型实参
                }
                if (paramType.IsRequiredModifier)
                {
                    paramType = (paramType as RequiredModifierType).ElementType;
                }
                if (!externTypeToId.ContainsKey(paramType))
                {
                    throw new Exception("externTypeToId do not exist key: " + paramType
                        + ", while process parameter of method: " + method);
                }
                writer.Write(externTypeToId[paramType]);
            }
        }
    }

```

查找泛型参数的实参:

因为这里外层函数是泛型函数的引用，它是明确类型的，例如调用一个泛型函数，调用的时候所有的泛型都有明确的类型，而查找的就是这个明确的类型，例如： 

```c#
class SampleClass<T>         // 在定义的时候 T 是泛型
{
    void Swap(T t1, T t2) { }   // 函数的参数是泛型T，该泛型来自于所属的类型
}

SampleClass<string> a;         // 在初始化类型的时候，是要求明确泛型的类型的
a.Swap("string1", "string2");  // 因此在调用泛型函数的时候，参数中的参数如果是来自于类型，那么就可以反查找到该泛型的类型
```



具体逻辑如下：

```c#
        /// <summary>
        /// 以contextType为上下文，查找泛型参数对应的实参
        /// </summary>
        /// <param name="gp">泛型参数</param>
        /// <param name="contextType">上下文类型</param>
        /// <returns></returns>
        public static TypeReference ResolveGenericArgument(this GenericParameter gp, TypeReference contextType)
        {
            if (contextType.IsGenericInstance)  // 是泛型实例，GenericInstanceType
            {
                var genericIns = ((GenericInstanceType)contextType);
                var genericTypeRef = genericIns.ElementType;  // 泛型类型的引用
                var genericTypeDef = genericTypeRef.Resolve(); // 泛型类型的定义
                for (int i = 0; i < genericTypeRef.GenericParameters.Count; i++) // 类型的泛型列表
                {
                    if (genericTypeRef.GenericParameters[i] == gp) // 如果参数的泛型类型 == 类型的泛型类型
                    {
                        return genericIns.GenericArguments[i];   // 返回类型的泛型实参
                    }
                    if (genericTypeDef != null && genericTypeDef.GenericParameters[i] == gp)
                    {
                        return genericIns.GenericArguments[i];
                    }
                }
            }

            if (contextType.IsNested) // 嵌套类
            {
                return gp.ResolveGenericArgument(contextType.DeclaringType);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// 以contextMethod为上下文，查找泛型参数对应的实参
        /// </summary>
        /// <param name="gp">泛型参数</param>
        /// <param name="contextMethod">上下文函数</param>
        /// <returns></returns>
        public static TypeReference ResolveGenericArgument(this GenericParameter gp, MethodReference contextMethod)
        {
            if (contextMethod.IsGenericInstance)
            {
                var genericIns = contextMethod as GenericInstanceMethod;
                return genericIns.GenericArguments[gp.Position];
            }
            return null;
        }
```







## 补丁文件格式（二进制）

patch文件的格式如下，大体的结构就是：

* 文件头，魔术等
* 函数列表
  * 指令列表
    * 指令

参考源码：Source\VSProj\Src\Builder\FileVirtualMachineBuilder.cs

| Name                    | Type        | Note         |
| ----------------------- | ----------- | ------------ |
| instructionMagic        | UInt64      | 317431043901 |
| interfaceBridgeTypeName | const char* |              |
| externTypeCount         | Int32       |              |
| methodCount             | Int32       |              |

下面开始是methods集合，遍历method,size为methodCount:

| Name     | Type  | Note |
| -------- | ----- | ---- |
| codeSize | Int32 |      |

下面开始是method的Instruction集合，遍历code，size为codeSize:

| Name    | Type  | Note |
| ------- | ----- | ---- |
| Code    | Int32 |      |
| Operand | Int32 |      |

这样就拥有的热更新的二进制补丁文件，接下里只需在动态运行时下发这个二进制补丁文件，加载到iFix的VirtualMachine中，即可实现热更新功能。

在下一节里面，我们将重点研究iFix自己实现的这个VirtualMachine的，来看看这个虚拟机内部是如何去执行这些补丁文件里面的IL指令来实现一个简单的C#虚拟机，这里面重要需要解决的一个疑惑就是：众所周知Mono的C#热更新是很简单的，因为C#本身就很容易通过反射来实现这样的功能，但在il2cpp下的C#热更新就比较复杂了，最常见的问题在il2cpp中的C#是不支持反射接口的，并且每个C#函数在转成CPP的时候都会在其函数名后面增加一串数字，导致每次打包这些函数签名都是变化的，导致很难实现热更新功能，但iFix就能很巧妙的绕开这些限制来实现也支持il2cpp的C#热更新机制。
