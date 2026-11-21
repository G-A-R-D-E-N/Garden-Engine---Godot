namespace OpenCommonwealth.Services.Papyrus.Vm;

public static class EvidenceKeys
{
    public const string Frames = "vm.frames.explicit_stack";
    public const string Return = "vm.control_flow.return";
    public const string Parameters = "vm.parameters.passing";
    public const string NativeTarget = "vm.native.target_contract";
    public const string Assignment = "vm.assignment.defaults";
    public const string Arithmetic = "vm.arithmetic.comparisons";
    public const string Types = "vm.types.casts_and_tests";
    public const string Dispatch = "vm.dispatch.property_and_function";
    public const string Arrays = "vm.arrays.semantics";
    public const string Structs = "vm.structs.semantics";
    public const string Waits = "vm.latent.waits";
    public const string States = "vm.states.gotostate";
    public const string Variables = "vm.variables.instance_storage";
    public const string OnInitBarrier = "vm.lifecycle.oninit_barrier";
    public const string EventDuringOnInit = "vm.lifecycle.event_during_oninit";
    public const string SchedulerBudget = "vm.scheduler.instruction_budget";
    public const string Host = "vm.host.single_script_host";
    public const string FailClosed = "vm.errors.fail_closed";
}
