using DotnetAutomaticInterface;

namespace TestNuget;

[GenerateAutomaticInterface]
public class Test : ITest
{
    public string GetString()
    {
        return "works";
    }
}
