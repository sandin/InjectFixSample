

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
