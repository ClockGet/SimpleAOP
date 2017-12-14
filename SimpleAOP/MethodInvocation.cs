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
using System.Reflection;

namespace SimpleAOP
{
    public class MethodInvocation: IMethodInvocation
    {
        private readonly ParameterCollection _inputs;

        private readonly ParameterCollection _arguments;
        

        public IParameterCollection Inputs
        {
            get
            {
                return this._inputs;
            }
        }

        public IParameterCollection Arguments
        {
            get
            {
                return this._arguments;
            }
        }

        public object Target
        {
            get;
        }

        public MethodBase MethodBase
        {
            get;
        }

        public MethodInvocation(object target, MethodBase targetMethod, params object[] parameterValues)
        {
            if (targetMethod == null)
                throw new ArgumentException(nameof(targetMethod));
            this.Target = target;
            this.MethodBase = targetMethod;
            ParameterInfo[] targetParameters = targetMethod.GetParameters();
            this._arguments = new ParameterCollection(parameterValues, targetParameters, (ParameterInfo param) => true);
            this._inputs = new ParameterCollection(parameterValues, targetParameters, (ParameterInfo param) => !param.IsOut);
        }

        public IMethodReturn CreateMethodReturn(object returnValue, params object[] outputs)
        {
            return new MethodReturn(this, returnValue, outputs);
        }

        public IMethodReturn CreateExceptionMethodReturn(Exception ex)
        {
            return new MethodReturn(this, ex);
        }
    }
}
