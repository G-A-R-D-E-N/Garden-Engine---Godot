using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Dialogs;

public sealed record VoiceRef(FormKey Info, int ResponseNumber, FormKey? VoiceType, bool PlayerLine = false, FormKey? SharedInfo = null);
