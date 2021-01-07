# InjectFix实现原理(三) - 补丁格式

参考源码：Source\VSProj\Src\Builder\FileVirtualMachineBuilder.cs



## 补丁文件格式（二进制）

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

