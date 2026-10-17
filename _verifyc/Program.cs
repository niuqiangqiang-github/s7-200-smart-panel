using System;
using System.Linq;
using System.Reflection;
using S7.Net;

class P
{
    static void Main()
    {
        var t = typeof(Plc);
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => m.Name.Contains("Open") || m.Name.Contains("Close") || m.Name.Contains("Disconnect") || m.Name.Contains("Connect"))
                            .OrderBy(m => m.Name))
        {
            var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            Console.WriteLine(m.ReturnType.Name + " " + m.Name + "(" + ps + ")");
        }
    }
}
