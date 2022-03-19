# InjectFix实现原理(五)-加载补丁

在本节中，主要学习IFix是如何在运行时加载补丁的。



## 加载补丁文件

TODO



### 加载外部函数

`FileVirtualMachineBuilder.cs`

Patch文件中外部函数的结构体如下：

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
        bool isGeneric;           // ParameterType.HasGenericArgumentFromMethod
        string declaringType;     // ParameterType
    }
```

​      

从Patch文件中解析这些外部函数，并在运行时查找它们：

* 首先也是看这个函数是不是泛型函数的实例 `isGenericInstance`

```c#
static MethodBase readMethod(BinaryReader reader, Type[] externTypes)
{
    bool isGenericInstance = reader.ReadBoolean();
    BindingFlags flag = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.NonPublic | BindingFlags.Public;
    if (isGenericInstance)        // 泛型函数的实例（函数本身有泛型）
    {
        Type declaringType = externTypes[reader.ReadInt32()];     // 函数所属的类型
        string methodName = reader.ReadString();                  // 函数名
        //Console.WriteLine("load generic method: " + declaringType + " ?? " + methodName);
        int genericArgCount = reader.ReadInt32();                 
        //Console.WriteLine("genericArgCount:" + genericArgCount);
        Type[] genericArgs = new Type[genericArgCount];          // 函数的泛型列表
        for (int j = 0; j < genericArgCount; j++)
        {
            genericArgs[j] = externTypes[reader.ReadInt32()];     // 泛型的类型
            //Console.WriteLine(j + " ga:" + genericArgs[j]);
        }
        int paramCount = reader.ReadInt32();                      // 函数的参数列表
        object[] paramMatchInfo = new object[paramCount];
        for (int j = 0; j < paramCount; j++)
        {
            bool isGeneric = reader.ReadBoolean();                // 参数的泛型是否来自于函数的泛型
            paramMatchInfo[j] = isGeneric ? (object)reader.ReadString()  // 是：则直接读字符串
                                          : externTypes[reader.ReadInt32()]; // 否：则读一个Type的ID
        }
        
        // 根据patch中的外部函数信息，在运行时查找函数
        MethodInfo matchMethod = null;
        foreach (var method in declaringType.GetMethods(flag)) // 在运行时查找类型的函数列表(DeclaredOnly|Instance|Static|NoPublic|Public)
        {
            var paramInfos = method.GetParameters(); // 函数的参数列表  

            Type[] genericArgInfos = null;
            if (method.IsGenericMethodDefinition)    // 是不是泛型函数定义 
            {
                //UnityEngine.Debug.Log("get generic arg of "+ method);
                genericArgInfos = method.GetGenericArguments();  // 函数的泛型列表
            }
            bool paramMatch = paramInfos.Length == paramCount   // 对比函数的参数列表个数是否相同
                             && method.Name == methodName;      // 对比函数名是否相同
            if (paramMatch && genericArgCount > 0) // 如果patch中有泛型列表，则该函数必须是泛型函数，并且对比函数泛型的个数是否相同（注意：这里它不对比泛型的类型是不是相同，因此虽然patch中的泛型是明确的，但是这里是函数的定义，泛型是不明确的）
            {
                if (!method.IsGenericMethodDefinition || genericArgInfos.Length != genericArgCount)
                {
                    paramMatch = false;
                }
            }
            if (paramMatch)
            {
                for (int j = 0; j < paramCount; j++) // 遍历函数的参数列表
                {
                    // paramMatchInfo是patch文件中保存的函数参数列表，它可能是一个string(isGeneric=true), 也可能是一个类型(isGeneric=false)
                    string strMatchInfo = paramMatchInfo[j] as string;
                    if (strMatchInfo != null)
                    {
                        if (!method.IsGenericMethodDefinition)  // 如果函数有参数是泛型，那么该函数必须是泛型
                        {
                            paramMatch = false;
                            break;
                        }
                        // 从WriteMethod函数中，我们就得知了参数的 `isGeneric` 是指这个参数的泛型是不是来自于它定义的类型的泛型，如果是这种情况下，我们会直接保存这个泛型的Name，这里将index引用转为Type的Name(例如: `T`)
                        strMatchInfo = System.Text.RegularExpressions.Regex
                            .Replace(strMatchInfo, @"!!\d+", m =>
                                genericArgInfos[int.Parse(m.Value.Substring(2))].Name);
                        if (strMatchInfo != paramInfos[j].ParameterType.ToString()) // 对比参数的类型是否和参数泛型的泛型相同
                        {
                            //Console.WriteLine("gp not match:" + strMatchInfo + " ??? "
                            //    + paramInfos[j].ParameterType.ToString());
                            paramMatch = false;
                            break;
                        }
                    }
                    else
                    {
                        if ((paramMatchInfo[j] as Type) != paramInfos[j].ParameterType) // 对比参数的类型是否相同
                        {
                            //Console.WriteLine("pt not match:" + paramMatchInfo[j] + " ??? "
                            //    + paramInfos[j].ParameterType);
                            paramMatch = false;
                            break;
                        }
                    }
                }
            }
            if (paramMatch)
            {
                matchMethod = method;
                break;
            }
        }
        if (matchMethod == null)
        {
            throw new Exception("can not load generic method [" + methodName + "] of " + declaringType);
        }
        return matchMethod.MakeGenericMethod(genericArgs);
    }
    else
    {
        Type declaringType = externTypes[reader.ReadInt32()];
        string methodName = reader.ReadString();
        int paramCount = reader.ReadInt32();
        //Console.WriteLine("load no generic method: " + declaringType + " ?? " + methodName + " pc "
        //    + paramCount);
        Type[] paramTypes = new Type[paramCount];
        for (int j = 0; j < paramCount; j++)
        {
            paramTypes[j] = externTypes[reader.ReadInt32()];
        }
        bool isConstructor = methodName == ".ctor" || methodName == ".cctor";
        MethodBase externMethod = null;
        //StringBuilder sb = new StringBuilder();
        //sb.Append("method to find name: ");
        //sb.AppendLine(methodName);
        //for (int j = 0; j < paramCount; j++)
        //{
        //    sb.Append("p ");
        //    sb.Append(j);
        //    sb.Append(": ");
        //    sb.AppendLine(paramTypes[j].ToString());
        //}
        if (isConstructor)
        {
            externMethod = declaringType.GetConstructor(BindingFlags.Public | (methodName == ".ctor" ?
                BindingFlags.Instance : BindingFlags.Static) |
                BindingFlags.NonPublic, null, paramTypes, null);
            // : (MethodBase)(declaringType.GetMethod(methodName, paramTypes));
        }
        else
        {
            foreach (var method in declaringType.GetMethods(flag)) // 运行时遍历类型的函数列表，method是运行时的函数定义
            {
                if (method.Name == methodName            // 对比函数名是否相等
                    && !method.IsGenericMethodDefinition
                    && method.GetParameters().Length == paramCount) // 对比函数参数列表是否相等
                {
                    var methodParameterTypes = method.GetParameters().Select(p => p.ParameterType);
                    if (methodParameterTypes.SequenceEqual(paramTypes))  // 对比参数列表中每一个参数的类型是不是一一相等
                    {
                        externMethod = method;
                        break;
                    }
                    //else
                    //{
                    //    var mptlist = methodParameterTypes.ToList();
                    //    for (int j = 0; j < mptlist.Count; j++)
                    //    {
                    //        sb.Append("not match p ");
                    //        sb.Append(j);
                    //        sb.Append(": ");
                    //        sb.AppendLine(mptlist[j].ToString());
                    //    }
                    //}
                }
            }
        }
        if (externMethod == null)
        {
            throw new Exception("can not load method [" + methodName + "] of "
                + declaringType/* + ", info:\r\n" + sb.ToString()*/);
        }
        return externMethod;
    }
}

```

