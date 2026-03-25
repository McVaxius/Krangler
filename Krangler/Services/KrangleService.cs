using System;
using System.Collections.Generic;

namespace Krangler.Services;

public static class KrangleService
{
    private static readonly string[] ExerciseWords =
    {
        "Pushup", "Squat", "Lunge", "Plank", "Burpee", "Crunch", "Deadlift",
        "Curl", "Press", "Pullup", "Shrug", "Thrust", "Bridge", "Flutter",
        "Situp", "Sprawl", "Kata", "Kihon", "Kumite", "Ukemi", "Breakfall",
        "Sweep", "Roundhouse", "Jab", "Hook", "Cross", "Uppercut", "Parry",
        "Block", "Guard", "Stance", "Strike", "Punch", "Kick", "Elbow",
        "Knee", "Clinch", "Throw", "Grapple", "Armbar", "Choke", "Dodge",
        "Weave", "Slip", "Roll", "Feint", "Riposte", "Sprint", "Bench",
        "Clean", "Snatch", "Jerk", "Row", "Dip", "Step", "Jump", "Dash",
        "March", "Drill", "Crawl", "Climb", "Planche", "Muscle", "Lever",
        "Pistol", "Dragon", "Crane", "Tiger", "Mantis", "Viper", "Eagle",
    };

    private static readonly Dictionary<string, string> Cache = new();

    public static void ClearCache() => Cache.Clear();

    public static string KrangleName(string originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName)) return originalName;
        if (Cache.TryGetValue(originalName, out var cached)) return cached;

        var atIdx = originalName.IndexOf('@');
        var charPart = atIdx >= 0 ? originalName[..atIdx] : originalName;
        var serverPart = atIdx >= 0 ? originalName[(atIdx + 1)..] : "";

        var hash = GetStableHash(charPart);
        var rng = new Random(hash);

        var nameParts = charPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = ExerciseWords[rng.Next(ExerciseWords.Length)];
        var last = nameParts.Length > 1 ? ExerciseWords[rng.Next(ExerciseWords.Length)] : "";

        if (first.Length > 14) first = first[..14];
        if (last.Length > 14) last = last[..14];
        if (last.Length > 0 && first.Length + 1 + last.Length > 22)
            last = last[..Math.Max(1, 22 - first.Length - 1)];

        var result = last.Length > 0 ? $"{first} {last}" : first;

        if (!string.IsNullOrEmpty(serverPart))
        {
            var serverHash = GetStableHash(serverPart);
            var serverRng = new Random(serverHash);
            var serverWord = ExerciseWords[serverRng.Next(ExerciseWords.Length)];
            if (serverWord.Length > 25) serverWord = serverWord[..25];
            result = $"{result}@{serverWord}";
        }

        Cache[originalName] = result;
        return result;
    }

    private static readonly string[] FCWords =
    {
        "GYM", "FIT", "REP", "SET", "MAX", "PRO", "ZEN", "OHM",
        "KAI", "RYU", "TAO", "CHI", "POW", "ARC", "OAK", "ASH",
    };

    private static readonly string[] TitleWords =
    {
        "The Swole", "The Ripped", "Gains Incarnate", "The Buffed",
        "Master of Reps", "The Shredded", "Iron Will", "Steel Thighs",
        "The Absolute Unit", "Cardio King", "Flex Champion", "The Yolked",
        "Dumbbell Sage", "The Juiced", "Barbell Lord", "Kettlebell Saint",
    };

    public static string KrangleFCTag(string originalTag)
    {
        if (string.IsNullOrWhiteSpace(originalTag)) return originalTag;
        var key = $"fc:{originalTag}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // Extract special characters from start and end
        var start = ExtractLeadingSpecialChars(originalTag);
        var end = ExtractTrailingSpecialChars(originalTag);
        var middle = originalTag[start.Length..^end.Length];

        // Krangle only the middle content
        var krangledMiddle = KrangleMiddleContent(middle, FCWords);
        var result = start + krangledMiddle + end;

        Cache[key] = result;
        return result;
    }

    public static string KrangleTitle(string originalTitle)
    {
        if (string.IsNullOrWhiteSpace(originalTitle)) return originalTitle;
        var key = $"title:{originalTitle}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // Extract special characters from start and end
        var start = ExtractLeadingSpecialChars(originalTitle);
        var end = ExtractTrailingSpecialChars(originalTitle);
        var middle = originalTitle[start.Length..^end.Length];

        // Krangle only the middle content
        var krangledMiddle = KrangleMiddleContent(middle, TitleWords);
        var result = start + krangledMiddle + end;

        Cache[key] = result;
        return result;
    }

    public static string KrangleServer(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName)) return serverName;
        var key = $"srv:{serverName}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var hash = GetStableHash(serverName);
        var rng = new Random(hash);
        var word = ExerciseWords[rng.Next(ExerciseWords.Length)];
        if (word.Length > 25) word = word[..25];

        Cache[key] = word;
        return word;
    }

    // Helper methods to preserve special characters in FC tags and titles
    private static string ExtractLeadingSpecialChars(string input)
    {
        var end = 0;
        while (end < input.Length && !char.IsLetterOrDigit(input[end]))
            end++;
        return input[..end];
    }

    private static string ExtractTrailingSpecialChars(string input)
    {
        var start = input.Length;
        while (start > 0 && !char.IsLetterOrDigit(input[start - 1]))
            start--;
        return input[start..];
    }

    private static string KrangleMiddleContent(string middle, string[] wordPool)
    {
        if (string.IsNullOrWhiteSpace(middle)) return middle;
        
        var hash = GetStableHash(middle);
        var rng = new Random(hash);
        var word = wordPool[rng.Next(wordPool.Length)];
        
        return word;
    }

    private static int GetStableHash(string input)
    {
        unchecked
        {
            int hash = 17;
            foreach (var c in input)
                hash = hash * 31 + c;
            return hash;
        }
    }
}
