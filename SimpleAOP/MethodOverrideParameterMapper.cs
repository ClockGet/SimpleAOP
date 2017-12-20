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
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleAOP
{
    internal class MethodOverrideParameterMapper
    {
        private readonly MethodInfo _methodToOverride;

        private GenericParameterMapper _genericParameterMapper;

        public Type[] GenericMethodParameters
        {
            get
            {
                return this._genericParameterMapper.GetGeneratedParameters();
            }
        }

        public MethodOverrideParameterMapper(MethodInfo methodToOverride)
        {
            this._methodToOverride = methodToOverride;
        }

        public void SetupParameters(MethodBuilder methodBuilder, GenericParameterMapper parentMapper)
        {
            if (this._methodToOverride.IsGenericMethod)
            {
                Type[] genericArguments = this._methodToOverride.GetGenericArguments();
                string[] names = (from t in genericArguments
                                  select t.Name).ToArray();
                GenericTypeParameterBuilder[] builders = methodBuilder.DefineGenericParameters(names);
                for (int i = 0; i < genericArguments.Length; i++)
                {
                    builders[i].SetGenericParameterAttributes(genericArguments[i].GenericParameterAttributes);
                    Type[] source = (from ct in genericArguments[i].GetGenericParameterConstraints()
                                     select parentMapper.Map(ct)).ToArray();
                    Type[] interfaceConstraints = (from t in source
                                                   where t.IsInterface
                                                   select t).ToArray();
                    Type baseConstraint = (from t in source
                                           where !t.IsInterface
                                           select t).FirstOrDefault();
                    if (baseConstraint != (Type)null)
                    {
                        builders[i].SetBaseTypeConstraint(baseConstraint);
                    }
                    if (interfaceConstraints.Length != 0)
                    {
                        builders[i].SetInterfaceConstraints(interfaceConstraints);
                    }
                }
                this._genericParameterMapper = new GenericParameterMapper(genericArguments, builders.Cast<Type>().ToArray(), parentMapper);
            }
            else
            {
                this._genericParameterMapper = parentMapper;
            }
        }

        public Type GetParameterType(Type originalParameterType)
        {
            return this._genericParameterMapper.Map(originalParameterType);
        }

        public Type GetElementType(Type originalParameterType)
        {
            return this.GetParameterType(originalParameterType).GetElementType();
        }

        public Type GetReturnType()
        {
            return this.GetParameterType(this._methodToOverride.ReturnType);
        }
    }
}
