// Loads the skills from THIS folder and gives the chat bot ONE tool to use them: load_skill(skillName).
//
//   Skills/
//     SkillLoader.cs                  <- this file
//     summarize-document/SKILL.md     <- one folder per skill
//     compare-documents/SKILL.md
//
// A SKILL.md starts with a small header ("front matter"), then the instructions:
//
//   ---
//   name: summarize-document
//   description: Use when the user asks for a summary of one uploaded file.
//   ---
//   1. Search for ...
//
// HOW SKILLS WORK - "progressive disclosure"
//   1. The model always sees a short MENU: each skill's name + description (in the tool description).
//   2. When a request matches a skill, the model calls load_skill("summarize-document").
//   3. The tool returns the full instructions and the model follows them.
//   Only the skills that are needed enter the conversation, so you can have many without a huge prompt.
//
// To add a skill: create Skills/<name>/SKILL.md. No code change needed.

using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace AgentRag.Api.Skills;

public sealed class SkillLoader
{
    public const string ToolName = "load_skill";

    private const string SkillsFolder = "Skills";
    private const string SkillFileName = "SKILL.md";
    private const string Fence = "---";

    private readonly Dictionary<string, Skill> _skills = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);

    public SkillLoader(ILogger<SkillLoader> logger)
    {
        // bin/Debug/net10.0/Skills - the .csproj copies the SKILL.md files next to the dll.
        string folder = Path.Combine(AppContext.BaseDirectory, SkillsFolder);
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("No skills folder at {Folder}.", folder);
            return;
        }

        foreach (string file in Directory.GetFiles(folder, SkillFileName, SearchOption.AllDirectories))
        {
            try
            {
                Skill skill = Parse(File.ReadAllText(file), file);
                _skills[skill.Name] = skill;
            }
            catch (FormatException ex)
            {
                logger.LogWarning("Skipping skill {File}: {Reason}", file, ex.Message);   // one bad file never stops the app
            }
        }

        logger.LogInformation("Loaded {Count} skill(s): {Names}.", _skills.Count, string.Join(", ", _skills.Keys));
    }

    public IReadOnlyCollection<Skill> Skills
    {
        get { return _skills.Values; }
    }

    /// <summary>The load_skill tool for the chat model, or null when there are no skills.</summary>
    public AITool? CreateLoadSkillTool()
    {
        if (_skills.Count == 0)
        {
            return null;
        }

        return AIFunctionFactory.Create(LoadSkill, ToolName, BuildToolDescription());
    }

    // The body of the tool. The model sees the parameter name and its [Description].
    private string LoadSkill([Description("The exact skill name from the list, for example summarize-document.")] string skillName)
    {
        Skill? skill;
        if (_skills.TryGetValue(skillName.Trim(), out skill))
        {
            return skill.Instructions;
        }

        // Tell the model what went wrong so it can retry with a real name.
        return "Unknown skill '" + skillName + "'. Available skills: " + string.Join(", ", _skills.Keys) + ".";
    }

    // The "menu" the model reads to decide WHEN to load a skill.
    private string BuildToolDescription()
    {
        StringBuilder text = new StringBuilder();
        text.AppendLine("Loads the step-by-step instructions of a skill.");
        text.AppendLine("Call it BEFORE answering whenever the user's request matches one of these skills, then follow the instructions it returns:");
        foreach (Skill skill in _skills.Values)
        {
            text.AppendLine("- " + skill.Name + ": " + skill.Description);
        }

        return text.ToString();
    }

    // SKILL.md text -> Skill. The header is a tiny subset of YAML: one "key: value" per line.
    private static Skill Parse(string text, string filePath)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != Fence)
        {
            throw new FormatException("must start with a '---' header.");
        }

        Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int closingLine = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line == Fence)
            {
                closingLine = i;
                break;
            }

            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                fields[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim().Trim('"', '\'');
            }
        }

        if (closingLine < 0)
        {
            throw new FormatException("the header has no closing '---'.");
        }

        // name defaults to the folder name: Skills/summarize-document/SKILL.md -> summarize-document
        string? name;
        if (!fields.TryGetValue("name", out name) || name.Length == 0)
        {
            name = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? ".").Name;
        }

        string? description;
        if (!fields.TryGetValue("description", out description) || description.Length == 0)
        {
            throw new FormatException("'description' is required in the header.");
        }

        string instructions = string.Join("\n", lines, closingLine + 1, lines.Length - closingLine - 1).Trim();
        if (instructions.Length == 0)
        {
            throw new FormatException("there are no instructions below the header.");
        }

        return new Skill(name, description, instructions);
    }
}
