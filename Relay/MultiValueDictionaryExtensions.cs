using System;
using System.Collections.Generic;

namespace Relay
{
    public static class MultiValueDictionaryExtensions
    {
        public static ConcurrentMultiDictionary<TKey, TValue> ToMultiValueDictionary<TInput, TKey, TValue>(this IEnumerable<TInput> collection, Func<TInput, TKey> keySelector, Func<TInput, TValue> valueSelector)
        {
            var dictionary = new ConcurrentMultiDictionary<TKey, TValue>();
            foreach (var item in collection)
            {
                dictionary.Add(keySelector(item), valueSelector(item));
            }
            return dictionary;
        }
    }
}