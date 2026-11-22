// One skill = one SKILL.md file: a name, a short description, and step-by-step instructions.

namespace AgentRag.Api.Skills;

public sealed class Skill
{
    public Skill(string name, string description, string instructions)
    {
        Name = name;
        Description = description;
        Instructions = instructions;
    }

    /// <summary>For example "summarize-document". The model asks for a skill by this name.</summary>
    public string Name { get; }

    /// <summary>When to use the skill. The ONLY part the model sees before it loads the skill.</summary>
    public string Description { get; }

    /// <summary>The Markdown body: what the model should do, step by step.</summary>
    public string Instructions { get; }
}
