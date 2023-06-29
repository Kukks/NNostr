using System;
using System.Collections.Generic;

namespace NNostr.Tests;

public static class EnumerableExtensions
{
    private static Random random = new Random();

    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
    {
        List<T> list = new List<T>(source);
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = random.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
        return list;
    }
}