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

namespace SimpleAOP
{
    internal class GenericParameterMapper
    {
        private static readonly KeyValuePair<Type, Type>[] EmptyMappings = new KeyValuePair<Type, Type>[0];

        private readonly IDictionary<Type, Type> _mappedTypesCache = new Dictionary<Type, Type>();

        private readonly ICollection<KeyValuePair<Type, Type>> _localMappings;

        private readonly GenericParameterMapper _parent;

        public static GenericParameterMapper DefaultMapper
        {
            get;
        } = new GenericParameterMapper(Type.EmptyTypes, Type.EmptyTypes, null);


        public GenericParameterMapper(Type type, GenericParameterMapper parent)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (type.IsGenericType)
            {
                if (type.IsGenericTypeDefinition)
                {
                    throw new ArgumentException("Cannot Map Generic Type Definition");
                }
                this._parent = parent;
                this._localMappings = GenericParameterMapper.CreateMappings(type.GetGenericTypeDefinition().GetGenericArguments(), type.GetGenericArguments());
            }
            else
            {
                this._localMappings = GenericParameterMapper.EmptyMappings;
                this._parent = null;
            }
        }

        public GenericParameterMapper(Type[] reflectedParameters, Type[] generatedParameters)
            : this(reflectedParameters, generatedParameters, null)
        {
        }

        public GenericParameterMapper(Type[] reflectedParameters, Type[] generatedParameters, GenericParameterMapper parent)
        {
            this._parent = parent;
            this._localMappings = GenericParameterMapper.CreateMappings(reflectedParameters, generatedParameters);
        }

        private static ICollection<KeyValuePair<Type, Type>> CreateMappings(Type[] reflectedParameters, Type[] generatedParameters)
        {
            if (reflectedParameters == null)
                throw new ArgumentNullException(nameof(reflectedParameters));
            if (generatedParameters == null)
                throw new ArgumentNullException(nameof(generatedParameters));
            if (reflectedParameters.Length != generatedParameters.Length)
            {
                throw new ArgumentException("Mapped Parameters DoNot Match");
            }
            List<KeyValuePair<Type, Type>> mappings = new List<KeyValuePair<Type, Type>>();
            for (int i = 0; i < reflectedParameters.Length; i++)
            {
                mappings.Add(new KeyValuePair<Type, Type>(reflectedParameters[i], generatedParameters[i]));
            }
            return mappings;
        }

        public Type Map(Type typeToMap)
        {
            Type mappedType = default(Type);
            if (!this._mappedTypesCache.TryGetValue(typeToMap, out mappedType))
            {
                mappedType = this.DoMap(typeToMap);
                this._mappedTypesCache[typeToMap] = mappedType;
            }
            return mappedType;
        }

        private Type DoMap(Type typeToMap)
        {
            if (!typeToMap.IsGenericParameter)
            {
                if (typeToMap.IsArray)
                {
                    Type mappedElementType = this.Map(typeToMap.GetElementType());
                    if (typeToMap.GetArrayRank() != 1)
                    {
                        return mappedElementType.MakeArrayType(typeToMap.GetArrayRank());
                    }
                    return mappedElementType.MakeArrayType();
                }
                if (typeToMap.IsGenericType)
                {
                    Type[] mappedGenericArguments = (from gp in typeToMap.GetGenericArguments()
                                                     select this.Map(gp)).ToArray();
                    return typeToMap.GetGenericTypeDefinition().MakeGenericType(mappedGenericArguments);
                }
                return typeToMap;
            }
            Type mappedType = (from kvp in this._localMappings
                               where kvp.Key == typeToMap
                               select kvp.Value).FirstOrDefault() ?? typeToMap;
            if (this._parent != null)
            {
                mappedType = this._parent.Map(mappedType);
            }
            return mappedType;
        }

        public Type[] GetReflectedParameters()
        {
            return (from kvp in this._localMappings
                    select kvp.Key).ToArray();
        }

        public Type[] GetGeneratedParameters()
        {
            return (from kvp in this._localMappings
                    select kvp.Value).ToArray();
        }
    }
}
