# InjectFix实现原理(三) - 补丁格式

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

# arg[0]: command 
-patch   

# arg[1]: core_assmbly_path 
./Assets/Plugins/IFix.Core.dl

# arg[2]: injected_assmbly_path
./Library/ScriptAssemblies/Assembly-CSharp.dll
null 

# arg[3]: config_path
./process_cfg

# arg[4]: patch_file_output_path
Assembly-CSharp.patch.bytes

# search_path
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
    // 目标方法
    MethodDefinition method = callee as MethodDefinition;
    
    // 如果是dll之外的方法，或者是构造函数，析构函数，作为虚拟机之外（extern）的方法
    Id = addExternMethod(callee, caller),
    
    // 包含不支持指令的方法，作为虚拟机之外（extern）的方法
}

//原生方法的引用
int addExternMethod(MethodReference callee, MethodDefinition caller)
{
    // ...
    addExternType(callee.DeclaringType);
    foreach (var p in callee.Parameters)
    {
        addExternType(resolveType);
    }
    
    // ...
    
    int methodId = externMethods.Count;
    externMethodToId[callee] = methodId;
    externMethods.Add(callee);
    return methodId;
}

//再补丁新增一个对原生方法的引用
int addExternType(TypeReference type, TypeReference contextType = null)
{
    // ...
    externTypes.Add(type);
    return externTypes.Count - 1;
}
```







## 补丁文件格式（二进制）

参考源码：Source\VSProj\Src\Builder\FileVirtualMachineBuilder.cs

| Name                    | Type        | Note         |
| ----------------------- | ----------- | ------------ |
| instructionMagic        | UInt64      | 317431043901 |
| interfaceBridgeTypeName | const char* |              |
| externTypeCount         | Int32       |              |
| methodCount             | Int32       |              |

下面开始遍历method,size为methodCount:

| Name     | Type  | Note |
| -------- | ----- | ---- |
| codeSize | Int32 |      |

下面开始遍历code，size为codeSize:

| Name    | Type  | Note |
| ------- | ----- | ---- |
| Code    | Int32 |      |
| Operand | Int32 |      |

