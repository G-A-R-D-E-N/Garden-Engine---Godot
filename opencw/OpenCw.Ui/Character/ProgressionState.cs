using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record ProgressionSaveState(int Level, double XpIntoLevel, int PerkPoints, IReadOnlyList<int> Attributes,
                                          IReadOnlyDictionary<string, int>? PerkRanks = null);
