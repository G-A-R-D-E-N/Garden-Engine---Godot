namespace OpenCommonwealth.Services.Interaction;

// Invariant: Unknown means the ownership data could not be read; it is never a denial, so a caller must not report theft or trespass on it.
public enum OwnershipDecision { Allowed, Denied, Unknown }
