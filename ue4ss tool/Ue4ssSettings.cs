using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ue4ss_tool;

/// <summary>
/// <c>UE4SS-settings.ini</c> 의 콘솔 창 설정을 켠다.
/// <para>
/// 기본 패키지(zDEV 가 아닌 것)는 <c>[Debug]</c> 의 세 값이 모두 0 이라, 게임을 켜도 UE4SS 창이 하나도 뜨지 않는다.
/// 설치·업데이트 때마다 1 로 고정한다. 다른 줄(주석·다른 설정·줄바꿈 방식·BOM)은 그대로 둔다.
/// </para>
/// </summary>
public static class Ue4ssSettings
{
    public const string DebugSection = "Debug";

    /// <summary>로그 콘솔 창, GUI 창 사용, GUI 창을 처음부터 보이기.</summary>
    internal static readonly string[] ConsoleKeys = { "ConsoleEnabled", "GuiConsoleEnabled", "GuiConsoleVisible" };

    /// <summary>파일의 콘솔 창 설정을 켠다. 바꾼 것이 있으면 true.</summary>
    public static bool ForceConsoleOn(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var preamble = Encoding.UTF8.GetPreamble();
        var hasBom = bytes.AsSpan().StartsWith(preamble);
        var encoding = new UTF8Encoding(hasBom);
        var skip = hasBom ? preamble.Length : 0;
        var text = encoding.GetString(bytes, skip, bytes.Length - skip);

        var patched = ForceConsoleOn(text, out var changed);
        if (!changed) return false;
        File.WriteAllText(path, patched, encoding);
        return true;
    }

    /// <summary>
    /// <c>[Debug]</c> 의 콘솔 키를 모두 <c>1</c> 로 바꾼다. 없는 키는 <c>[Debug]</c> 바로 아래에,
    /// <c>[Debug]</c> 가 없으면 끝에 섹션째 붙인다.
    /// </summary>
    internal static string ForceConsoleOn(string text, out bool changed)
    {
        changed = false;
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var endsWithNewline = text.EndsWith(newline);
        var lines = text.Length == 0 ? new List<string>() : text.Split(newline).ToList();
        if (endsWithNewline) lines.RemoveAt(lines.Count - 1);

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var debugHeader = -1;
        var inDebug = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inDebug = trimmed[1..^1].Trim().Equals(DebugSection, StringComparison.OrdinalIgnoreCase);
                if (inDebug && debugHeader < 0) debugHeader = i;
                continue;
            }
            if (!inDebug || trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;

            var eq = lines[i].IndexOf('=');
            if (eq < 0) continue;
            var key = lines[i][..eq].Trim();
            if (!ConsoleKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;

            found.Add(key);
            if (lines[i][(eq + 1)..].Trim() == "1") continue;
            var indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
            lines[i] = $"{indent}{key} = 1";
            changed = true;
        }

        var missing = ConsoleKeys.Where(k => !found.Contains(k)).Select(k => $"{k} = 1").ToList();
        if (missing.Count > 0)
        {
            if (debugHeader >= 0)
            {
                lines.InsertRange(debugHeader + 1, missing);
            }
            else
            {
                if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
                lines.Add($"[{DebugSection}]");
                lines.AddRange(missing);
            }
            changed = true;
        }

        var result = string.Join(newline, lines);
        return endsWithNewline ? result + newline : result;
    }
}
