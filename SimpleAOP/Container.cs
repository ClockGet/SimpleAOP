using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleAOP
{
    public class AOPContainer
    {
        private static ConcurrentDictionary<Type, Type> derivedClasses = new ConcurrentDictionary<Type, Type>();
        private bool CanIntercept(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if(type.IsClass &&(type.IsPublic || type.IsNestedPublic) && type.IsVisible)
            {
                return !type.IsNested;
            }
            return false;
        }
        private Type CreateProxyType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (!CanIntercept(type))
                throw new InvalidOperationException($"the type '{type.Name}' cannot not be intercepted");
            Type typeToDerive = type;
            bool genericType = false;
            if (type.IsGenericType)
            {
                typeToDerive = type.GetGenericTypeDefinition();
                genericType = true;
            }
            Type interceptorType = derivedClasses.GetOrAdd(typeToDerive, t => new InterceptingClassGenerator(t).GenerateType());
            if (genericType)
            {
                interceptorType = interceptorType.MakeGenericType(type.GetGenericArguments());
            }
            return interceptorType;
        }
        public void Register(Type type)
        {
            CreateProxyType(type);
        }
        public void Register<T>()
        {
            Register(typeof(T));
        }
    }
}
