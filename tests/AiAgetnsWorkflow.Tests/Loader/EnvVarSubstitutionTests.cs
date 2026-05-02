using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Services;

namespace AiAgetnsWorkflow.Tests.Loader;

public class EnvVarSubstitutionTests : IDisposable
{
    private const string TestVar = "AIAGENTS_TEST_VAR_X";

    public void Dispose() => Environment.SetEnvironmentVariable(TestVar, null);

    [Fact]
    public void Apply_NoPlaceholders_ReturnsInputUnchanged()
    {
        EnvVarSubstitution.Apply("plain string").Should().Be("plain string");
    }

    [Fact]
    public void Apply_SinglePlaceholder_SubstitutesValue()
    {
        Environment.SetEnvironmentVariable(TestVar, "secret");
        EnvVarSubstitution.Apply($"Bearer ${{{TestVar}}}").Should().Be("Bearer secret");
    }

    [Fact]
    public void Apply_MultiplePlaceholders_SubstitutesAll()
    {
        Environment.SetEnvironmentVariable(TestVar, "X");
        EnvVarSubstitution.Apply($"${{{TestVar}}}-${{{TestVar}}}").Should().Be("X-X");
    }

    [Fact]
    public void Apply_LowerCaseVariable_DoesNotSubstitute()
    {
        EnvVarSubstitution.Apply("${lower_case}").Should().Be("${lower_case}");
    }

    [Fact]
    public void Apply_MissingVariable_ThrowsWithVarName()
    {
        var act = () => EnvVarSubstitution.Apply("${THIS_VAR_DOES_NOT_EXIST_XYZ_42}");
        act.Should().Throw<WorkflowValidationException>()
           .WithMessage("*THIS_VAR_DOES_NOT_EXIST_XYZ_42*");
    }
}
