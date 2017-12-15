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
        private ConstructorBuilder _staticConstructorBuilder;
        public InterceptionAttributeAnalysis(TypeBuilder typeBuilder, Type typeToIntercept)
        {
            _typeBuilder = typeBuilder;
            _typeToIntercept = typeToIntercept;
            _targetMethodInfos = GetMethodsToIntercept();
            if (!HasInterceptionAttribute(_typeToIntercept))
            {
                _targetMethodInfos = _targetMethodInfos.Where(type => HasInterceptionAttribute(type));
            }
            _staticConstructorBuilder = _typeBuilder.DefineConstructor(MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.HideBySig, CallingConventions.Standard, Type.EmptyTypes);
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
                foreach (var call in callHandlers.SelectMany(list => list.AsEnumerable()))
                {
                    builder.Use(call.Invoke);
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
        /*IL code
         * {
               // Method begins at RVA 0x24b0
               // Code size 80 (0x50)
               .maxstack 6
               .locals init (
                   [0] class ConsoleApplication15.MethodInvocation,
                   [1] class ConsoleApplication15.IMethodReturn
               )

               // MethodInvocation input = new MethodInvocation(this, Program.testInfo, a, b, c);
               IL_0000: ldarg.0
               IL_0001: ldsfld class [mscorlib]System.Reflection.MethodInfo ConsoleApplication15.Program::testInfo
               // (no C# code)
               IL_0006: ldc.i4.3
               IL_0007: newarr [mscorlib]System.Object
               IL_000c: dup
               IL_000d: ldc.i4.0
               IL_000e: ldarg.1
               IL_000f: box [mscorlib]System.Int32
               IL_0014: stelem.ref
               IL_0015: dup
               IL_0016: ldc.i4.1
               IL_0017: ldarg.2
               IL_0018: box [mscorlib]System.Int64
               IL_001d: stelem.ref
               IL_001e: dup
               IL_001f: ldc.i4.2
               IL_0020: ldarg.3
               IL_0021: stelem.ref
               IL_0022: newobj instance void ConsoleApplication15.MethodInvocation::.ctor(object, class [mscorlib]System.Reflection.MethodBase, object[])
               IL_0027: stloc.0
               // IMethodReturn r = this.@delegate(input);
               IL_0028: ldarg.0
               IL_0029: ldfld class ConsoleApplication15.InvokeHandlerDelegate ConsoleApplication15.Program::delegate
               IL_002e: ldloc.0
               IL_002f: callvirt instance class ConsoleApplication15.IMethodReturn ConsoleApplication15.InvokeHandlerDelegate::Invoke(class ConsoleApplication15.IMethodInvocation)
               IL_0034: stloc.1
               // if (r.Exception == null)
               IL_0035: ldloc.1
               IL_0036: callvirt instance class [mscorlib]System.Exception ConsoleApplication15.IMethodReturn::get_Exception()
               // (no C# code)
               IL_003b: brtrue.s IL_0049

               // return (int)r.ReturnValue;
               IL_003d: ldloc.1
               IL_003e: callvirt instance object ConsoleApplication15.IMethodReturn::get_ReturnValue()
               IL_0043: unbox.any [mscorlib]System.Int32
               // (no C# code)
               IL_0048: ret

               // throw r.Exception;
               IL_0049: ldloc.1
               IL_004a: callvirt instance class [mscorlib]System.Exception ConsoleApplication15.IMethodReturn::get_Exception()
               // (no C# code)
               IL_004f: throw
            } // end of method Program::Test
         */
        private void AddMethod(MethodInfo methodInfo, InvokeHandlerDelegate @delegate)
        {
            var methodBuilder = _typeBuilder.DefineMethod(methodInfo.Name, methodInfo.Attributes, methodInfo.ReturnType, methodInfo.GetParameters().Select(p => p.ParameterType).ToArray());
            DefineGenericArguments(methodBuilder, methodInfo);
            var il = methodBuilder.GetILGenerator();
            il.DeclareLocal(typeof(MethodInvocation));  //local 0
            il.DeclareLocal(typeof(IMethodReturn));     //local 1
            il.Emit(OpCodes.Ldarg_0);   //load this

            var parameterInfos = methodInfo.GetParameters();
            int parameterLength = parameterInfos.Length;
            //new object[]
            il.Emit(OpCodes.Ldc_I4, parameterLength);
            il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < parameterLength; i++)
            {
                il.Emit(OpCodes.Dup);//copy the value to the top of the stack
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg, i + 1);
                if (parameterInfos[i].ParameterType.IsValueType)
                {
                    il.Emit(OpCodes.Box, parameterInfos[i].ParameterType);
                }
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Newobj, typeof(MethodInvocation).GetConstructor(new Type[] { typeof(object), typeof(MethodBase), typeof(object[]) }));//call new MethodInvocation()
            il.Emit(OpCodes.Stloc_0);//set new obj to local 0

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
            if (method != (MethodInfo)null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) && method.IsVirtual && !method.IsFinal)
            {
                return method.DeclaringType != typeof(object);
            }
            return false;
        }

    }
}
