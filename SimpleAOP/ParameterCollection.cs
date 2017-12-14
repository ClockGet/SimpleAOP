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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SimpleAOP
{
    public class ParameterCollection : IParameterCollection, IList, ICollection, IEnumerable
    {
        private struct ArgumentInfo
        {
            public readonly int Index;

            public readonly string Name;

            public readonly ParameterInfo ParameterInfo;

            public ArgumentInfo(int index, ParameterInfo parameterInfo)
            {
                this.Index = index;
                this.Name = parameterInfo.Name;
                this.ParameterInfo = parameterInfo;
            }
        }

        private readonly List<ArgumentInfo> _argumentInfo;

        private readonly object[] _arguments;

        public object this[string parameterName]
        {
            get
            {
                return this._arguments[this._argumentInfo[this.IndexForInputParameterName(parameterName)].Index];
            }
            set
            {
                this._arguments[this._argumentInfo[this.IndexForInputParameterName(parameterName)].Index] = value;
            }
        }

        public object this[int index]
        {
            get
            {
                return this._arguments[this._argumentInfo[index].Index];
            }
            set
            {
                this._arguments[this._argumentInfo[index].Index] = value;
            }
        }

        public bool IsReadOnly
        {
            get;
        }

        public bool IsFixedSize
        {
            get;
        } = true;


        public int Count
        {
            get
            {
                return this._argumentInfo.Count;
            }
        }

        public object SyncRoot
        {
            get
            {
                return this;
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return false;
            }
        }

        public ParameterCollection(object[] arguments, ParameterInfo[] argumentInfo, Predicate<ParameterInfo> isArgumentPartOfCollection)
        {
            if (arguments == null)
                throw new ArgumentException(nameof(arguments));
            if (isArgumentPartOfCollection == null)
                throw new ArgumentException(nameof(isArgumentPartOfCollection));
            this._arguments = arguments;
            this._argumentInfo = new List<ArgumentInfo>();
            for (int argumentNumber = 0; argumentNumber < argumentInfo.Length; argumentNumber++)
            {
                if (isArgumentPartOfCollection(argumentInfo[argumentNumber]))
                {
                    this._argumentInfo.Add(new ArgumentInfo(argumentNumber, argumentInfo[argumentNumber]));
                }
            }
        }

        private int IndexForInputParameterName(string paramName)
        {
            for (int i = 0; i < this._argumentInfo.Count; i++)
            {
                if (this._argumentInfo[i].Name == paramName)
                {
                    return i;
                }
            }
            throw new ArgumentException("Invalid parameter Name", "paramName");
        }

        public ParameterInfo GetParameterInfo(int index)
        {
            return this._argumentInfo[index].ParameterInfo;
        }

        public ParameterInfo GetParameterInfo(string parameterName)
        {
            return this._argumentInfo[this.IndexForInputParameterName(parameterName)].ParameterInfo;
        }

        public string ParameterName(int index)
        {
            return this._argumentInfo[index].Name;
        }

        public bool ContainsParameter(string parameterName)
        {
            return this._argumentInfo.Any((ArgumentInfo info) => info.Name == parameterName);
        }

        public int Add(object value)
        {
            throw new NotSupportedException();
        }

        public bool Contains(object value)
        {
            return this._argumentInfo.Exists(delegate (ArgumentInfo info)
            {
                object obj = this._arguments[info.Index];
                if (obj == null)
                {
                    return value == null;
                }
                return obj.Equals(value);
            });
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public int IndexOf(object value)
        {
            return this._argumentInfo.FindIndex((ArgumentInfo info) => this._arguments[info.Index].Equals(value));
        }

        public void Insert(int index, object value)
        {
            throw new NotSupportedException();
        }

        public void Remove(object value)
        {
            throw new NotSupportedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        public void CopyTo(Array array, int index)
        {
            int destIndex = 0;
            this._argumentInfo.GetRange(index, this._argumentInfo.Count - index).ForEach(delegate (ArgumentInfo info)
            {
                array.SetValue(this._arguments[info.Index], destIndex);
                int num = ++destIndex;
            });
        }

        public IEnumerator GetEnumerator()
        {
            int num;
            for (int i = 0; i < this._argumentInfo.Count; i = num)
            {
                yield return this._arguments[this._argumentInfo[i].Index];
                num = i + 1;
            }
        }
    }
}
