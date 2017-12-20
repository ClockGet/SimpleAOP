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
        private GenericParameterMapper _targetTypeParameterMapper;
        private int methodCount;
        public InterceptionAttributeAnalysis(TypeBuilder typeBuilder, Type typeToIntercept, GenericParameterMapper targetTypeParameterMapper)
        {
            _typeBuilder = typeBuilder;
            _typeToIntercept = typeToIntercept;
            _targetMethodInfos = GetMethodsToIntercept();
            if (!HasInterceptionAttribute(_typeToIntercept))
            {
                _targetMethodInfos = _targetMethodInfos.Where(methodInfo => HasInterceptionAttribute(methodInfo));
            }
            var targetType = _typeToIntercept;
            if(_typeToIntercept.IsGenericType)
            {
                targetType = ((ModuleBuilder)_typeBuilder.Module).DefineType($"__Anonymous__GenericType__{_typeToIntercept.Name}",TypeAttributes.Public|TypeAttributes.BeforeFieldInit|TypeAttributes.Sealed).CreateType();
            }
            var mapType = typeof(ProxyMap<>).MakeGenericType(targetType);
            _mapFieldInfo = mapType.GetField("Map", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _proxyDelegates = (List<ProxyDelegate>)_mapFieldInfo.GetValue(null);
            _mapGetMethod = _mapFieldInfo.FieldType.GetMethod("get_Item", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new Type[] { typeof(int) }, new ParameterModifier[0]);
            methodCount = _proxyDelegates.Count;
            _targetTypeParameterMapper = targetTypeParameterMapper;
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
                foreach (var list in callHandlers)
                {
                    if (list == null)
                        continue;
                    foreach (var call in list)
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
            var methodBuilder = _typeBuilder.DefineMethod(methodInfo.Name, attr);
            
            Type declaringType = methodInfo.DeclaringType;
            GenericParameterMapper mapper = (declaringType.IsGenericType && declaringType != methodInfo.ReflectedType) ? new GenericParameterMapper(declaringType, _targetTypeParameterMapper) : _targetTypeParameterMapper;
            MethodOverrideParameterMapper paraMapper = new MethodOverrideParameterMapper(methodInfo);
            paraMapper.SetupParameters(methodBuilder, mapper);
            methodBuilder.SetReturnType(paraMapper.GetReturnType());
            methodBuilder.SetParameters(methodInfo.GetParameters().Select(pi => paraMapper.GetParameterType(pi.ParameterType)).ToArray());
            var il = methodBuilder.GetILGenerator();
            il.DeclareLocal(typeof(ProxyDelegate));     //local 0
            il.DeclareLocal(typeof(MethodInvocation));  //local 1
            il.DeclareLocal(typeof(IMethodReturn));     //local 2
            //add ProxyDelegate to ProxyMap
            _proxyDelegates.Add(new ProxyDelegate { @delegate = @delegate, methodInfo = methodInfo });

            var parameterInfos = methodInfo.GetParameters();
            int parameterLength = parameterInfos.Length;
            int paramNum = 1;
            foreach (ParameterInfo pi2 in parameterInfos)
            {
                methodBuilder.DefineParameter(paramNum++, pi2.Attributes, pi2.Name);
            }
            //the ref parameter(ref or out) set a default value
            for (int i = 0; i < parameterLength; i++)
            {
                if (paraMapper.GetParameterType(parameterInfos[i].ParameterType).IsByRef)
                {
                    EmitLoadArgument(il, i);
                    il.Emit(OpCodes.Initobj, paraMapper.GetElementType(parameterInfos[i].ParameterType));
                }
            }
            //ProxyDelegate proxyDelegate=ProxyMap<$Type>.Map[$methodCount];
            il.Emit(OpCodes.Ldsfld, _mapFieldInfo);
            EmitLoadConstant(il, methodCount);
            il.Emit(OpCodes.Callvirt, _mapGetMethod);
            il.Emit(OpCodes.Stloc_0);                   //set the value to local 0

            il.Emit(OpCodes.Ldarg_0);   //load this
            il.Emit(OpCodes.Ldloc_0);   //load local 0
            il.Emit(OpCodes.Ldfld, typeof(ProxyDelegate).GetField("methodInfo"));

            //new object[]
            EmitLoadConstant(il, parameterLength);
            il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < parameterLength; i++)
            {
                il.Emit(OpCodes.Dup);//copy the value to the top of the stack
                EmitLoadConstant(il, i);
                EmitLoadArgument(il, i);
                var parameterType = paraMapper.GetParameterType(parameterInfos[i].ParameterType);
                var isByRef = parameterType.IsByRef;
                if (isByRef)
                    parameterType = parameterType.GetElementType();
                if (parameterInfos[i].ParameterType.IsValueType || parameterInfos[i].ParameterType.IsGenericParameter)
                {
                    il.Emit(OpCodes.Box, paraMapper.GetParameterType( parameterInfos[i].ParameterType));
                }
                else if (isByRef)
                {
                    il.Emit(OpCodes.Ldobj, parameterType);
                    if (parameterType.IsValueType || parameterType.IsGenericParameter)
                    {
                        il.Emit(OpCodes.Box, parameterType);
                    }
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
            var getItem = typeof(IParameterCollection).GetMethod("get_Item", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(int) }, new ParameterModifier[0]);
            int refParamNums = 0;
            for (int i = 0; i < parameterLength; i++)
            {
                var parameterType = paraMapper.GetParameterType(parameterInfos[i].ParameterType);
                if (parameterType.IsByRef)
                {
                    EmitLoadArgument(il, i);
                    il.Emit(OpCodes.Ldloc_2);
                    il.Emit(OpCodes.Callvirt, getOutputs);
                    EmitLoadConstant(il, refParamNums++);
                    il.Emit(OpCodes.Callvirt, getItem);
                    var elementType = parameterType.GetElementType();
                    if (elementType.IsValueType || elementType.IsGenericParameter)
                    {
                        il.Emit(OpCodes.Unbox_Any, elementType);
                    }
                    else
                    {
                        il.Emit(OpCodes.Castclass, elementType);
                    }
                    il.Emit(OpCodes.Stobj, elementType);
                }
            }
            //return ($ReturnType)r.ReturnValue;
            var returnType = paraMapper.GetReturnType();
            if (returnType != typeof(void))
            {
                il.Emit(OpCodes.Ldloc_2);
                il.Emit(OpCodes.Callvirt, typeof(IMethodReturn).GetMethod("get_ReturnValue"));
                if (returnType.IsValueType || returnType.IsGenericParameter)
                    il.Emit(OpCodes.Unbox_Any, returnType);
                else
                    il.Emit(OpCodes.Castclass, returnType);
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
        #region Utility
        //   Original C# code written by
        //   Unity - https://github.com/unitycontainer/unity
        //	 Copyright (C) 2015-2017 Microsoft
        // 
        //   Licensed under the Apache License, Version 2.0 (the "License"); you may not use this product except in 
        //   compliance with the License. You may obtain a copy of the License at
        //
        //   http://www.apache.org/licenses/LICENSE-2.0
        //
        //   Unless required by applicable law or agreed to in writing, software distributed under the License is 
        //   distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
        //   See the License for the specific language governing permissions and limitations under the License.
        //
        private static readonly OpCode[] LoadConstOpCodes = new OpCode[9]
        {
            OpCodes.Ldc_I4_0,
            OpCodes.Ldc_I4_1,
            OpCodes.Ldc_I4_2,
            OpCodes.Ldc_I4_3,
            OpCodes.Ldc_I4_4,
            OpCodes.Ldc_I4_5,
            OpCodes.Ldc_I4_6,
            OpCodes.Ldc_I4_7,
            OpCodes.Ldc_I4_8
        };
        private static readonly OpCode[] LoadArgsOpCodes = new OpCode[3]
        {
            OpCodes.Ldarg_1,
            OpCodes.Ldarg_2,
            OpCodes.Ldarg_3
        };
        private static void EmitLoadConstant(ILGenerator il, int i)
        {
            if (i < LoadConstOpCodes.Length)
            {
                il.Emit(LoadConstOpCodes[i]);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, i);
            }
        }
        private static void EmitLoadArgument(ILGenerator il, int argumentNumber)
        {
            if (argumentNumber < LoadArgsOpCodes.Length)
            {
                il.Emit(LoadArgsOpCodes[argumentNumber]);
            }
            else
            {
                il.Emit(OpCodes.Ldarg, argumentNumber + 1);
            }
        }
        #endregion

    }
}
