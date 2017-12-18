using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleAOP
{
    internal class InterceptionAttributeAnalysis
    {
        private const int _capacity = 15;
        private TypeBuilder _typeBuilder;
        private Type _typeToIntercept;
        private IEnumerable<MethodInfo> _targetMethodInfos;
        private List<ProxyDelegate> _proxyDelegates;
        private FieldInfo _mapFieldInfo;
        private MethodInfo _mapGetMethod;
        private int methodCount;
        public InterceptionAttributeAnalysis(TypeBuilder typeBuilder, Type typeToIntercept)
        {
            _typeBuilder = typeBuilder;
            _typeToIntercept = typeToIntercept;
            _targetMethodInfos = GetMethodsToIntercept();
            if (!HasInterceptionAttribute(_typeToIntercept))
            {
                _targetMethodInfos = _targetMethodInfos.Where(methodInfo => HasInterceptionAttribute(methodInfo));
            }
            var mapType = typeof(ProxyMap<>).MakeGenericType(_typeToIntercept);
            _mapFieldInfo = mapType.GetField("Map", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _proxyDelegates = (List<ProxyDelegate>)_mapFieldInfo.GetValue(null);
            _mapGetMethod = _mapFieldInfo.FieldType.GetMethod("get_Item", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new Type[] { typeof(int) }, new ParameterModifier[0]);
            methodCount = _proxyDelegates.Count;
        }
        public void Analysis()
        {
            HandlerAttribute[] globalAttributes = _typeToIntercept.GetCustomAttributes<HandlerAttribute>(true).ToArray();
            List<ICallHandler>[] callHandlers = new List<ICallHandler>[_capacity];
            foreach (var method in _targetMethodInfos)
            {
                var _localAttributes = method.GetCustomAttributes<HandlerAttribute>(true).ToArray();
                foreach (var attr in globalAttributes.Concat(_localAttributes))
                {
                    var index = attr.Order & _capacity;
                    var list = callHandlers[index];
                    if (list == null)
                    {
                        list = new List<ICallHandler>();
                        callHandlers[index] = list;
                    }
                    list.Add(attr.CreateHandler());
                }
                ProxyBuilder builder = new ProxyBuilder(ProxyBuilder.ConvertToDelegate(method));
                foreach(var list in callHandlers)
                {
                    if (list == null)
                        continue;
                    foreach(var call in list)
                    {
                        builder.Use(call.Invoke);
                    }
                }
                AddMethod(method, builder.Build());
                Array.Clear(callHandlers, 0, _capacity);
            }
        }
        /*Derived Method process:
         * baseMethod.ReturnType DerivedMethod(baseMethod.Parameters)
         * {
         *     IMethodInvocation input = Wrapped(baseMethod.Parameters);
         *     var methodReturn = @delegate(input);
         *     if(methodReturn.Exception==null)
         *         return (baseMethod.ReturnType)methodReturn.ReturnValue;
         *     throw methodReturn.Exception;
         * }
         */
        private void AddMethod(MethodInfo methodInfo, InvokeHandlerDelegate @delegate)
        {
            var attr = methodInfo.Attributes & ~MethodAttributes.VtableLayoutMask & ~MethodAttributes.Abstract;
            var methodBuilder = _typeBuilder.DefineMethod(methodInfo.Name, attr, methodInfo.ReturnType, methodInfo.GetParameters().Select(p => p.ParameterType).ToArray());
            DefineGenericArguments(methodBuilder, methodInfo);
            var il = methodBuilder.GetILGenerator();
            il.DeclareLocal(typeof(ProxyDelegate));     //local 0
            il.DeclareLocal(typeof(MethodInvocation));  //local 1
            il.DeclareLocal(typeof(IMethodReturn));     //local 2
            //add ProxyDelegate to ProxyMap
            _proxyDelegates.Add(new ProxyDelegate { @delegate = @delegate, methodInfo = methodInfo });

            var parameterInfos = methodInfo.GetParameters();
            int parameterLength = parameterInfos.Length;
            //the ref parameter(ref or out) set a default value
            for (int i = 0; i < parameterLength; i++)
            {
                if (parameterInfos[i].ParameterType.IsByRef)
                {
                    il.Emit(OpCodes.Ldarg, i + 1);
                    il.Emit(OpCodes.Initobj, parameterInfos[i].ParameterType.GetElementType());
                }
            }
            //ProxyDelegate proxyDelegate=ProxyMap<$Type>.Map[$methodCount];
            il.Emit(OpCodes.Ldsfld, _mapFieldInfo);
            il.Emit(OpCodes.Ldc_I4, methodCount);
            il.Emit(OpCodes.Callvirt, _mapGetMethod);
            il.Emit(OpCodes.Stloc_0);                   //set the value to local 0

            il.Emit(OpCodes.Ldarg_0);   //load this
            il.Emit(OpCodes.Ldloc_0);   //load local 0
            il.Emit(OpCodes.Ldfld, typeof(ProxyDelegate).GetField("methodInfo"));

            //new object[]
            il.Emit(OpCodes.Ldc_I4, parameterLength);
            il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < parameterLength; i++)
            {
                il.Emit(OpCodes.Dup);//copy the value to the top of the stack
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg, i + 1);
                var parameterType = parameterInfos[i].ParameterType;
                var isByRef = parameterType.IsByRef;
                if (isByRef)
                    parameterType = parameterType.GetElementType();
                if (parameterInfos[i].ParameterType.IsValueType || parameterInfos[i].ParameterType.IsGenericParameter)
                {
                    if (isByRef)
                        il.Emit(OpCodes.Ldobj, parameterType);
                    il.Emit(OpCodes.Box, parameterType);
                }
                else if (isByRef)
                {
                    il.Emit(OpCodes.Ldind_Ref);
                }
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Newobj, typeof(MethodInvocation).GetConstructor(new Type[] { typeof(object), typeof(MethodBase), typeof(object[]) }));//call new MethodInvocation()
            il.Emit(OpCodes.Stloc_1);//set new obj to local 1
            //IMethodReturn r=proxyDelegate.@delegate(input)
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldfld, typeof(ProxyDelegate).GetField("delegate"));
            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Callvirt, @delegate.GetType().GetMethod("Invoke"));
            il.Emit(OpCodes.Stloc_2);
            // if (r.Exception==null)
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Callvirt, typeof(IMethodReturn).GetMethod("get_Exception", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance));
            Label lbTrue = il.DefineLabel();
            il.Emit(OpCodes.Brtrue_S, lbTrue);
            //false
            //handle ref or out parameters
            var getOutputs = typeof(IMethodReturn).GetMethod("get_Outputs", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var getItem = typeof(IParameterCollection).GetMethod("get_Item", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string) }, new ParameterModifier[0]);
            for (int i = 0; i < parameterLength; i++)
            {
                var parameterType = parameterInfos[i].ParameterType;
                if (parameterType.IsByRef)
                {
                    il.Emit(OpCodes.Ldarg, i + 1);
                    il.Emit(OpCodes.Ldloc_2);
                    il.Emit(OpCodes.Callvirt, getOutputs);
                    il.Emit(OpCodes.Ldstr, parameterInfos[i].Name);
                    il.Emit(OpCodes.Callvirt, getItem);
                    if (parameterType.IsValueType || parameterType.IsGenericParameter)
                    {
                        var elementType = parameterType.GetElementType();
                        il.Emit(OpCodes.Unbox_Any, elementType);
                        il.Emit(OpCodes.Stobj, elementType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Castclass, parameterType.GetElementType());
                        il.Emit(OpCodes.Stind_Ref);
                    }
                }
            }
            //return ($ReturnType)r.ReturnValue;
            if (methodInfo.ReturnType != typeof(void))
            {
                il.Emit(OpCodes.Ldloc_2);
                il.Emit(OpCodes.Callvirt, typeof(IMethodReturn).GetMethod("get_ReturnValue"));
                if (methodInfo.ReturnType.IsValueType || methodInfo.ReturnType.IsGenericParameter)
                    il.Emit(OpCodes.Unbox_Any, methodInfo.ReturnType);
            }
            il.Emit(OpCodes.Ret);
            //ture
            //throw r.Exception
            il.MarkLabel(lbTrue);
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Callvirt, typeof(IMethodReturn).GetMethod("get_Exception", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance));
            il.Emit(OpCodes.Throw);
            //increase methodCount
            methodCount++;

        }
        private static void DefineGenericArguments(MethodBuilder methodBuilder, MethodInfo baseMethod)
        {
            if (!baseMethod.IsGenericMethod)
            {
                return;
            }
            Type[] genericArguments = baseMethod.GetGenericArguments();
            GenericTypeParameterBuilder[] genericTypes = methodBuilder.DefineGenericParameters((from t in genericArguments
                                                                                                select t.Name).ToArray());
            for (int i = 0; i < genericArguments.Length; i++)
            {
                genericTypes[i].SetGenericParameterAttributes(genericArguments[i].GenericParameterAttributes);
                List<Type> interfaceConstraints = new List<Type>();
                Type[] genericParameterConstraints = genericArguments[i].GetGenericParameterConstraints();
                foreach (Type constraint in genericParameterConstraints)
                {
                    if (constraint.IsClass)
                    {
                        genericTypes[i].SetBaseTypeConstraint(constraint);
                    }
                    else
                    {
                        interfaceConstraints.Add(constraint);
                    }
                }
                if (interfaceConstraints.Count > 0)
                {
                    genericTypes[i].SetInterfaceConstraints(interfaceConstraints.ToArray());
                }
            }
        }
        private bool HasInterceptionAttribute(Type type)
        {
            return type.GetCustomAttributes(typeof(HandlerAttribute), true)?.Length > 0;
        }
        private bool HasInterceptionAttribute(MethodInfo methodInfo)
        {
            return methodInfo.GetCustomAttributes(typeof(HandlerAttribute), true)?.Length > 0;
        }
        private IEnumerable<MethodInfo> GetMethodsToIntercept()
        {
            MethodInfo[] methods = _typeToIntercept.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (MethodInfo methodInfo in methods)
            {
                if (!methodInfo.IsSpecialName && MethodCanBeIntercepted(methodInfo))
                {
                    yield return methodInfo;
                }
            }
        }

        private static bool MethodCanBeIntercepted(MethodInfo method)
        {
            if (method != (MethodInfo)null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) && method.IsVirtual)
            {
                return method.DeclaringType != typeof(object);
            }
            return false;
        }

    }
}
