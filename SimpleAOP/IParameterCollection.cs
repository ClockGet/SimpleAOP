﻿//   Original C# code written by
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

using System.Reflection;

namespace SimpleAOP
{
    public interface IParameterCollection
    {
        object this[string parameterName]
        {
            get;
            set;
        }
        object this[int index]
        {
            get;
            set;
        }

        string ParameterName(int index);

        ParameterInfo GetParameterInfo(int index);

        ParameterInfo GetParameterInfo(string parameterName);

        bool ContainsParameter(string parameterName);
    }
}
