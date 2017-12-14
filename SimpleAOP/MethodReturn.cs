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
    public class MethodReturn : IMethodReturn
    {
        private readonly ParameterCollection _outputs;

        public IParameterCollection Outputs
        {
            get
            {
                return this._outputs;
            }
        }

        public object ReturnValue
        {
            get;
            set;
        }

        public Exception Exception
        {
            get;
            set;
        }

        public MethodReturn(IMethodInvocation originalInvocation, object returnValue, object[] arguments)
        {
            if (originalInvocation == null)
                throw new ArgumentException(nameof(originalInvocation));
            this.ReturnValue = returnValue;
            this._outputs = new ParameterCollection(arguments, originalInvocation.MethodBase.GetParameters(), (ParameterInfo pi) => pi.ParameterType.IsByRef);
        }

        public MethodReturn(IMethodInvocation originalInvocation, Exception exception)
        {
            if (originalInvocation == null)
                throw new ArgumentException(nameof(originalInvocation));
            this.Exception = exception;
            this._outputs = new ParameterCollection(new object[0], new ParameterInfo[0], (ParameterInfo _) => false);
        }
    }
}
