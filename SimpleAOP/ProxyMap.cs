using System.Collections.Generic;
using System.Reflection;

namespace SimpleAOP
{
    public class ProxyMap<T>
    {
        public static List<ProxyDelegate> Map = new List<ProxyDelegate>();
    }
    public class ProxyDelegate
    {
        public MethodInfo methodInfo;
        public InvokeHandlerDelegate @delegate;
    }
}
