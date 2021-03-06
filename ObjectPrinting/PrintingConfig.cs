using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace ObjectPrinting
{
    public class PrintingConfig<TOwner> : IPrintingConfig
    {
        private const int MaxNestingLevel = 5;
        private static readonly HashSet<Type> FinalTypes = new HashSet<Type>
        {
            typeof(int), typeof(double), typeof(float), typeof(string),
            typeof(long), typeof(short), typeof(sbyte), typeof(byte),
            typeof(uint), typeof(ulong), typeof(ushort), typeof(decimal),
            typeof(DateTime), typeof(TimeSpan), typeof(char), typeof(bool)
        };

        private readonly HashSet<Type> excludingTypes;
        private readonly HashSet<string> excludingProperties;
        private readonly Dictionary<Type, Delegate> alternativeSerializersForTypes;
        private readonly Dictionary<string, Delegate> alternativeSerializersForProperties;
        Dictionary<Type, Delegate> IPrintingConfig.AlternativeSerializersForTypes => alternativeSerializersForTypes;
        Dictionary<string, Delegate> IPrintingConfig.AlternativeSerializersForProperties => alternativeSerializersForProperties;

        public PrintingConfig()
        {
            excludingTypes = new HashSet<Type>();
            excludingProperties = new HashSet<string>();
            alternativeSerializersForTypes = new Dictionary<Type, Delegate>();
            alternativeSerializersForProperties = new Dictionary<string, Delegate>();
        }

        public PropertyPrintingConfig<TOwner, TPropType> Printing<TPropType>()
        {
            return new PropertyPrintingConfig<TOwner, TPropType>(this);
        }

        public PropertyPrintingConfig<TOwner, TPropType> Printing<TPropType>(Expression<Func<TOwner, TPropType>> memberSelector)
        {
            if (memberSelector.Body is MemberExpression memberExpression)
                return new PropertyPrintingConfig<TOwner, TPropType>(this, memberExpression.Member.Name);
            throw new ArgumentException("No MemberExpression in argument");
        }

        public PrintingConfig<TOwner> Excluding<TPropType>(Expression<Func<TOwner, TPropType>> memberSelector)
        {
            if (memberSelector.Body is MemberExpression memberExpression)
                excludingProperties.Add(memberExpression.Member.Name);
            return this;
        }

        internal PrintingConfig<TOwner> Excluding<TPropType>()
        {
            excludingTypes.Add(typeof(TPropType));
            return this;
        }

        public string PrintToString(TOwner obj)
        {
            return PrintToString(obj, 0, new HashSet<object>());
        }

        private string PrintToString(object obj, int nestingLevel, HashSet<object> parents)
        {
            if (obj == null)
                return "null" + Environment.NewLine;

            if (FinalTypes.Contains(obj.GetType()))
                return obj + Environment.NewLine;

            if (parents.Contains(obj))
                return "Cycle reference" + Environment.NewLine;

            if (nestingLevel > MaxNestingLevel)
                return "Nesting level is exceeded" + Environment.NewLine;

            return GetObjectSerialization(obj, nestingLevel, parents);
        }

        private string GetObjectSerialization(object obj, int nestingLevel, HashSet<object> parents)
        {
            parents.Add(obj);
            var sb = new StringBuilder();
            var type = obj.GetType();
            sb.AppendLine(type.Name);
            string serialization;
            if (obj is IDictionary iDictionary)
                serialization = GetDictionarySerialization(iDictionary, nestingLevel, parents);
            else if (obj is ICollection iCollection)
                serialization = GetCollectionSerialization(iCollection, nestingLevel, parents);
            else
            {
                var memberInfos = type.GetMembers(BindingFlags.Public | BindingFlags.Instance);
                serialization = GetMembersSerialization(obj, nestingLevel, memberInfos, parents);
            }

            sb.Append(serialization);
            parents.Remove(obj);
            return sb.ToString();
        }

        private string GetDictionarySerialization(IDictionary dictionary, int nestingLevel, HashSet<object> parents)
        {
            var indentation = new string('\t', nestingLevel + 1);
            var sb = new StringBuilder();
            var i = 0;
            foreach (var key in dictionary.Keys)
            {
                var printResult = PrintToString(key, nestingLevel + 1, parents);
                sb.AppendFormat("{0}Key {1}: {2}", indentation, i, printResult);
                printResult = PrintToString(dictionary[key], nestingLevel + 1, parents);
                sb.AppendFormat("{0}Value {1}: {2}", indentation, i, printResult);
                i++;
            }

            return sb.ToString();
        }

        private string GetCollectionSerialization(ICollection collection, int nestingLevel, HashSet<object> parents)
        {
            var indentation = new string('\t', nestingLevel + 1);
            var sb = new StringBuilder();
            var i = 0;
            foreach (var element in collection)
            {
                var printResult = PrintToString(element, nestingLevel + 1, parents);
                sb.AppendFormat("{0}Element {1}: {2}", indentation, i, printResult);
                i++;
            }

            return sb.ToString();
        }

        private string GetMembersSerialization(object obj, int nestingLevel, MemberInfo[] memberInfos, HashSet<object> parents)
        {
            var sb = new StringBuilder();
            foreach (var memberInfo in memberInfos)
            {
                if (!(memberInfo is PropertyInfo || memberInfo is FieldInfo))
                    continue;
                var member = new Member(memberInfo, obj);
                if (IsExcludedMember(member))
                    continue;
                var serialization = GetMemberSerialization(member, nestingLevel, parents);
                serialization = serialization.PadLeft(nestingLevel + 1 + serialization.Length, '\t');
                sb.Append(serialization);
            }

            return sb.ToString();
        }

        private bool IsExcludedMember(Member member)
        {
            return excludingProperties.Contains(member.Name) || excludingTypes.Contains(member.Type);
        }

        private string GetMemberSerialization(Member member, int nestingLevel, HashSet<object> parents)
        {
            var memberValue = member.Value;
            if (TryGetAlternativeSerializer(member, out var alternativeSerializer))
                memberValue = (string)alternativeSerializer.DynamicInvoke(member.Value);
            return $"{member.Name} = {PrintToString(memberValue, nestingLevel + 1, parents)}";
        }

        private bool TryGetAlternativeSerializer(Member member, out Delegate alternativeSerializer)
        {
            alternativeSerializer = null;
            if (alternativeSerializersForProperties.ContainsKey(member.Name))
                alternativeSerializer = alternativeSerializersForProperties[member.Name];
            else if (alternativeSerializersForTypes.ContainsKey(member.Type))
                alternativeSerializer = alternativeSerializersForTypes[member.Type];
            return alternativeSerializer != null;
        }
    }
}