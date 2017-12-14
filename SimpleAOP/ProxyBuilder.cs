using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleAOP
{
    internal class ProxyBuilder
    {
        private IList<Func<InvokeHandlerDelegate, InvokeHandlerDelegate>> delegateList = new List<Func<InvokeHandlerDelegate, InvokeHandlerDelegate>>();
        private InvokeHandlerDelegate _last;
        public ProxyBuilder(InvokeHandlerDelegate last)
        {
            _last = last;
        }
        public ProxyBuilder Use(Func<IMethodInvocation, InvokeHandlerDelegate, IMethodReturn> fun)
        {
            Func<InvokeHandlerDelegate, InvokeHandlerDelegate> func = (next) =>
             {
                 return new InvokeHandlerDelegate(input =>
                 {
                     return fun(input, next);
                 });
             };
            delegateList.Add(func);
            return this;
        }
        public InvokeHandlerDelegate Build()
        {
            InvokeHandlerDelegate @delegate = _last;
            foreach (var d in delegateList.Reverse())
            {
                @delegate = d(@delegate);
            }
            return @delegate;
        }
    }
}
