# InjectFix实现原理(四)  - IL虚拟机

InjectFix热更新的实现原理中最核心就是它通过去实现一个自己的IL虚拟机 ( `IFix.Core.VirtualMachine` )，支持了在运行时不管是mono还是il2cpp环境下都可以动态的加载和执行热更新的C#代码。

​                    

## 虚拟机

首先我们需要先了解一个概念：什么是虚拟机。这里的虚拟机都是指程序虚拟机（区别于VBox等系统虚拟机），它是指设计用来在平台无关的环境下运行程序代码的应用程序，例如Java虚拟机，C#虚拟机等，它们存在的目的都是为实现跨平台，一次编写，多处运行。Java程序运行在Java虚拟机里面，C#程序运行在C#虚拟机里面，而虚拟机本身也就只是一个程序，但它为了隔离硬件和平台的不同，虚拟出一整套程序运行所需的硬件架构，包括指令集，堆栈，寄存器等。

虚拟机主要需要解决的硬件和平台差异有：

* 指令集的不同。
* 寄存器的不同。
* 32位和64位的不同。

接下来我们来看虚拟机具体是如何处理这些差异的。

​               

## 寄存器和栈

为了解决硬件寄存器的差异问题，虚拟机一般而言都分为两种：基于栈实现的虚拟机和基于寄存器实现的虚拟机，例如在PC上运行的JVM虚拟机，就是基于栈的实现，因为不同的PC可能拥有不同的寄存器架构，它的优点是跨平台实现方便，只需要在栈上面去模拟寄存器的功能，而不需要去直接访问寄存器，但缺点也很明显就是速度相对于基于寄存器的而言会会慢一些。

在Android平台上运行的Java虚拟机（Dalvik或ART）则都是基于寄存器实现的虚拟机，它的优点显而易见就是快。但现实起来也麻烦一些，需要做平台相关的差异实现，但因为Android本身就是只在指定的硬件架构上运行的系统，因此为了追求更快的效率，Google基于寄存器重新实现了一个Java虚拟机。

在C#的世界里面，不管是微软官方的C#虚拟机还是第三方的mono虚拟机，它们都是基于栈实现的虚拟机。先记住这一点对于了解IL虚拟机的实现逻辑至关重要。

​                          

## IL指令集

虚拟机在解决寄存器的差异问题后，还需要解决CPU支持的指令集不同的问题，为了解决这个问题，所有的虚拟机都需要实现一套自己的指令集，这种一般都被称为“中间代码”，在Java里面是 `Bytecode`（俗称字节码），在C#里面是 `Microsoft Intermediate Language`（简称IL），Java代码或者C#代码都会被编译器先编译成中间代码，然后再通过虚拟机在运行时将中间代码翻译成二进制机器码在本地机器上运行，当然有些虚拟机为了追求性能，也支持预编译（ `AOT` ）和运行时编译（ `JIT`） 两种模式。

​        

首先我们先来大致了解一下IL代码具体是怎样的，这里以一个最简单的C#的helloworld程序为例，来看看C#代码转成IL代码的结果。

```c#
using System;

public class Program
{
	public static void Main(string[] args)
	{
		Console.WriteLine("Hello World!" + args);
	}
}
```

上面这段简单的代码转成IL代码之后变成如下这样的：

```C#
.method public static hidebysig cil managed ofname Main
	return: (void)
	params: (string[] args)
{
	// Code size (0x11)
	.maxstack 8
	IL_0000: ldstr "Hello World!"
	IL_0005: ldarg.0
	IL_0006: call System.String System.String::Concat(System.Object,System.Object)
	IL_000b: call System.Void System.Console::WriteLine(System.String)
	IL_0010: ret
}
```

这里我们可以看出来IL代码这种中间语言，相对于机器码而言还是非常易读的（有助记词的帮助下），它兼容了人读和机读的两者平衡，这段IL代码看起来几乎和它对应的C#代码差不多。这里先有一个大致的了解，后面我们将深入去了解这些IL指令具体是什么意思。

​      

## IL虚拟机

到此我们基本上对于虚拟机这个概念有了一个简单的认识，接下来我们来看如何在Unity里去实现一个自己的IL虚拟机，实现在运行时动态的加载和执行C#代码。

​            

一个虚拟机最重要的作用就是在一个平台无关的环境下去运行一段代码，而在IL虚拟机上就是指执行一段IL代码，因此这里我们最关心的就是现在运行在Unity环境下的虚拟机是如何执行IL代码的。

​                         

因为在Unity里面的存在mono和il2cpp两种完全不同的实现机制，因此虚拟机执行IL指令也分为两种情况：

* 在mono版本里，C#代码直接转成DLL（IL指令），由mono虚拟机在运行时执行IL指令。
* 在il2cpp版本里，C#代码先转成DLL（IL指令），然后通过il2cpp静态将IL指令转成CPP代码，再通过编译器编译成机器码，在运行时直接执行。

​                      

那么在Unity这里面就有两个地方是在直接去处理IL指令的：

* mono虚拟机在动态时执行IL指令。
* il2cpp在编译时静态将IL指令成C++代码。

第1种的mono情况很好理解，就是一个正常的虚拟机应该做的事情，在运行时一条一条的去执行IL指令就行了。而第2种情况，我们先抛开其他复杂的东西，可以简单的理解il2cpp只是将mono在运行时执行IL指令的这个过程前置到了编译时，在编译的过程中，il2cpp模拟出了一个IL虚拟机，一条一条的去处理这些IL指令，只是mono虚拟机是直接将IL指令转成了机器码去执行它，而il2cpp虚拟机则是将IL指令转成C++代码先保存起来，然后再通过C++的编译器将其转成机器码。但是如果抛开后面的流程，其实在mono和il2cpp处理IL指令的这个环节，它们的功能都是类似的，它们都是作为一个IL虚拟机去一条条的读取IL指令。

​         

我们要实现一个自己的IL虚拟机，首先就需要先去理解mono虚拟机和il2cpp虚拟机（简单把它也理解成一个虚拟机）是如何处理这些IL指令的。





TODO

```c++
// System.Void NewBehaviourScript::main(System.String[])
IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void NewBehaviourScript_main_m570D5FE78B8DAC520575C311A3978992E7C53737 (NewBehaviourScript_tF2FE3ECCFBC98B6EF49F3577A340114691B00003 * __this, StringU5BU5D_t933FB07893230EA91C40FF900D5400665E87B14E* ___args0, const RuntimeMethod* method)
{
	static bool s_Il2CppMethodInitialized;
	if (!s_Il2CppMethodInitialized)
	{
		il2cpp_codegen_initialize_method (NewBehaviourScript_main_m570D5FE78B8DAC520575C311A3978992E7C53737_MetadataUsageId);
		s_Il2CppMethodInitialized = true;
	}
	{
		StringU5BU5D_t933FB07893230EA91C40FF900D5400665E87B14E* L_3 = ___args0;
		String_t* L_4 = String_Concat_mBB19C73816BDD1C3519F248E1ADC8E11A6FDB495(_stringLiteral2EF7BDE608CE5404E97D5F042F95F89F1C232871, (RuntimeObject *)(RuntimeObject *)L_3, /*hidden argument*/NULL);
		IL2CPP_RUNTIME_CLASS_INIT(Console_t5C8E87BA271B0DECA837A3BF9093AC3560DB3D5D_il2cpp_TypeInfo_var);
		Console_WriteLine_mA5F7E391799514350980A0DE16983383542CA820(L_4, /*hidden argument*/NULL);
		return;
	}
}
```

