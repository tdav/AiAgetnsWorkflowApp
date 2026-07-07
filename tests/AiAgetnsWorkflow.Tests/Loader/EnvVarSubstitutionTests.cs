using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Services;

namespace AiAgetnsWorkflow.Tests.Loader;

// Env vars are process-global and TUnit runs tests in parallel,
// so every test uses its own variable name.
public class EnvVarSubstitutionTests
{
    [Test]
    public void Apply_NoPlaceholders_ReturnsInputUnchanged()
    {
        EnvVarSubstitution.Apply("plain string").Should().Be("plain string");
    }

    [Test]
    public void Apply_SinglePlaceholder_SubstitutesValue()
    {
        const string testVar = "AIAGENTS_TEST_VAR_SINGLE";
        try
        {
            Environment.SetEnvironmentVariable(testVar, "secret");
            EnvVarSubstitution.Apply($"Bearer ${{{testVar}}}").Should().Be("Bearer secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable(testVar, null);
        }
    }

    [Test]
    public void Apply_MultiplePlaceholders_SubstitutesAll()
    {
        const string testVar = "AIAGENTS_TEST_VAR_MULTI";
        try
        {
            Environment.SetEnvironmentVariable(testVar, "X");
            EnvVarSubstitution.Apply($"${{{testVar}}}-${{{testVar}}}").Should().Be("X-X");
        }
        finally
        {
            Environment.SetEnvironmentVariable(testVar, null);
        }
    }

    [Test]
    public void Apply_LowerCaseVariable_DoesNotSubstitute()
    {
        EnvVarSubstitution.Apply("${lower_case}").Should().Be("${lower_case}");
    }

    [Test]
    public void Apply_MissingVariable_ThrowsWithVarName()
    {
        var act = () => EnvVarSubstitution.Apply("${THIS_VAR_DOES_NOT_EXIST_XYZ_42}");
        act.Should().Throw<WorkflowValidationException>()
           .WithMessage("*THIS_VAR_DOES_NOT_EXIST_XYZ_42*");
    }
}
