using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCommonwealth.Services.Ui.Records;

public readonly record struct MovementRow(float Walk, float Run, float Sprint);

public sealed record MovementTypeSpeeds(MovementRow Forward, MovementRow Back, MovementRow Left, MovementRow Right)
{
    public const string Signature = "MOVT";

    public const string ForwardWalk = "MOVT\\ForwardWalk";
    public const string ForwardRun = "MOVT\\ForwardRun";
    public const string ForwardSprint = "MOVT\\ForwardSprint";
    public const string BackWalk = "MOVT\\BackWalk";
    public const string BackRun = "MOVT\\BackRun";
    public const string BackSprint = "MOVT\\BackSprint";
    public const string LeftWalk = "MOVT\\LeftWalk";
    public const string LeftRun = "MOVT\\LeftRun";
    public const string LeftSprint = "MOVT\\LeftSprint";
    public const string RightWalk = "MOVT\\RightWalk";
    public const string RightRun = "MOVT\\RightRun";
    public const string RightSprint = "MOVT\\RightSprint";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ForwardWalk, ForwardRun, ForwardSprint,
        BackWalk, BackRun, BackSprint,
        LeftWalk, LeftRun, LeftSprint,
        RightWalk, RightRun, RightSprint,
    };

    public static MovementTypeSpeeds Read(IRecordReader reader, string editorId, MovementTypeSpeeds fallback, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(fallback);
        if (reader.FindEditorId(editorId, Signature) is not { } id)
        {
            log?.Invoke($"MOVT {editorId} not found; using the built-in speeds.");
            return fallback;
        }
        var missed = 0;
        var speeds = new MovementTypeSpeeds(
            Row(reader, id, ForwardWalk, ForwardRun, ForwardSprint, fallback.Forward, ref missed),
            Row(reader, id, BackWalk, BackRun, BackSprint, fallback.Back, ref missed),
            Row(reader, id, LeftWalk, LeftRun, LeftSprint, fallback.Left, ref missed),
            Row(reader, id, RightWalk, RightRun, RightSprint, fallback.Right, ref missed));
        if (missed > 0)
            log?.Invoke($"MOVT {editorId}: {missed} of 12 speeds unreadable; those use the built-in values.");
        return speeds;
    }

    private static MovementRow Row(IRecordReader reader, string id, string walk, string run, string sprint, MovementRow fallback, ref int missed) => new(
        Value(reader, id, walk, fallback.Walk, ref missed),
        Value(reader, id, run, fallback.Run, ref missed),
        Value(reader, id, sprint, fallback.Sprint, ref missed));

    private static float Value(IRecordReader reader, string id, string path, float fallback, ref int missed)
    {
        if (float.TryParse(reader.Scalar(id, path), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) && value >= 0f)
            return value;
        missed++;
        return fallback;
    }
}
