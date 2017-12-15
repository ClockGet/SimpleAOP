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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleAOP
{
    internal class InterceptingClassGenerator
    {
        static readonly AssemblyBuilder assemblyBuilder;
        private Type _typeToIntercept;
        private TypeBuilder _typeBuilder;
        private Type _targetType;
        private GenericParameterMapper _mainTypeMapper;
        static InterceptingClassGenerator()
        {
            assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("DynamicClasses"), AssemblyBuilderAccess.Run);
        }
        public InterceptingClassGenerator(Type typeToIntercept)
        {
            this._typeToIntercept = typeToIntercept;
            CreateTypeBuilder();
        }
        public Type GenerateType()
        {
            AddConstructors();
            return _typeBuilder.CreateType();
        }
        private void AddConstructor(ConstructorInfo ctor)
        {
            if (!ctor.IsPublic && !ctor.IsFamily && !ctor.IsFamilyOrAssembly)
            {
                return;
            }
            MethodAttributes attributes = (ctor.Attributes & ~(MethodAttributes.RTSpecialName | MethodAttributes.HasSecurity | MethodAttributes.RequireSecObject) & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Public;
            ParameterInfo[] parameters = ctor.GetParameters();
            Type[] paramTypes = (from item in parameters
                                 select item.ParameterType).ToArray();
            ConstructorBuilder ctorBuilder = _typeBuilder.DefineConstructor(attributes, ctor.CallingConvention, paramTypes);
            for (int j = 0; j < parameters.Length; j++)
            {
                ctorBuilder.DefineParameter(j + 1, parameters[j].Attributes, parameters[j].Name);
            }
            ILGenerator il = ctorBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            for (int i = 0; i < paramTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, i + 1);
            }
            il.Emit(OpCodes.Call, ctor);
            il.Emit(OpCodes.Ret);
        }
        private void AddConstructors()
        {
            BindingFlags bindingFlags = (BindingFlags)(_typeToIntercept.IsAbstract ? BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic : BindingFlags.Public | BindingFlags.Instance);
            ConstructorInfo[] constructors = _typeToIntercept.GetConstructors(bindingFlags);
            foreach (ConstructorInfo ctor in constructors)
            {
                AddConstructor(ctor);
            }
        }
        
        private void CreateTypeBuilder()
        {
            TypeAttributes newAttributes = _typeToIntercept.Attributes;
            newAttributes = FilterTypeAttributes(newAttributes);
            Type baseClass = GetGenericType(_typeToIntercept);
            ModuleBuilder moduleBuilder = GetModuleBuilder();
            _typeBuilder = moduleBuilder.DefineType($"DynamicModule.Wrapped_{_typeToIntercept.Name}_{Guid.NewGuid().ToString("N")}", newAttributes, baseClass);
            _mainTypeMapper = DefineGenericArguments(_typeBuilder, baseClass);
            if (_typeToIntercept.IsGenericType)
            {
                Type definition = _typeToIntercept.GetGenericTypeDefinition();
                Type[] mappedParameters = (from t in definition.GetGenericArguments() select _mainTypeMapper.Map(t)).ToArray();
                _targetType = definition.MakeGenericType(mappedParameters);
            }
            else
            {
                _targetType = _typeToIntercept;
            }
        }
        private static Type GetGenericType(Type typeToIntercept)
        {
            if(typeToIntercept.IsGenericType)
            {
                return typeToIntercept.GetGenericTypeDefinition();
            }
            return typeToIntercept;
        }
        private static GenericParameterMapper DefineGenericArguments(TypeBuilder typeBuilder, Type baseClass)
        {
            if (!baseClass.IsGenericType)
            {
                return GenericParameterMapper.DefaultMapper;
            }
            Type[] genericArguments = baseClass.GetGenericArguments();
            GenericTypeParameterBuilder[] genericTypes = typeBuilder.DefineGenericParameters((from t in genericArguments
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
            return new GenericParameterMapper(genericArguments, genericTypes.Cast<Type>().ToArray());
        }
        private static TypeAttributes FilterTypeAttributes(TypeAttributes attributes)
        {
            if((attributes & TypeAttributes.NestedPublic)!=0)
            {
                attributes &= ~TypeAttributes.NestedPublic;
                attributes |= TypeAttributes.Public;
            }
            attributes &= ~TypeAttributes.ReservedMask;
            attributes &= ~TypeAttributes.Abstract;
            return attributes;
        }
        private static ModuleBuilder GetModuleBuilder()
        {
            string moduleName = Guid.NewGuid().ToString("N");
            return assemblyBuilder.DefineDynamicModule(moduleName);
        }
    }
}
