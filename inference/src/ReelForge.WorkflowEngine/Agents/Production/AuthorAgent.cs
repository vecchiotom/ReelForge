using Microsoft.Extensions.AI;
using ReelForge.Shared.Data.Models;
using ReelForge.Shared.Data.OutputSchemas;
using ReelForge.WorkflowEngine.Agents.Tools;

namespace ReelForge.WorkflowEngine.Agents.Production;

public class AuthorAgentImpl : ReelForgeAgentBase
{
    private const string DefaultPrompt =
        """
        You are the final assembler for Remotion video production. Take all scene data,
        the script, Remotion components, animation strategies, and the director's plan,
        and assemble them into a RenderManifestOutput JSON ready for Remotion rendering.

        Structure the output as follows:

        {
          "projectName": "string",  // Name/title of this video project
          "video": {
            "width": 1920,           // Video width in pixels
            "height": 1080,          // Video height in pixels
            "fps": 30,               // Frames per second
            "durationInFrames": 0    // Total video duration in frames
          },
          "compositions": [          // Array of Composition objects (not "scenes")
            {
              "id": "string",        // Unique composition identifier
              "componentName": "string",  // Name of the Remotion component
              "startFrame": 0,       // When this composition starts
              "durationInFrames": 0, // How long this composition runs
              "props": {},           // Props to pass to the Remotion component
              "script": {
                "voiceover": "string",      // Voiceover text for this composition
                "captions": ["string"]      // Array of caption text elements
              }
            }
          ],
          "assets": [                // Array of AssetReference objects
            {
              "id": "string",        // Unique asset identifier
              "type": "string",      // Asset type (e.g., "image", "video", "audio")
              "path": "string",      // Path/URL to the asset
              "properties": {}       // Additional asset metadata
            }
          ],
          "metadata": {}             // Additional project metadata
        }

        Ensure all timing is calculated in frames based on the specified fps. Calculate
        video.durationInFrames as the sum of all composition durations. Map all script
        content from the Scriptwriter to the appropriate compositions.

        Output as valid RenderManifestOutput JSON.
        """;

    public AuthorAgentImpl(
        IChatClient chatClient,
        IConfiguration configuration,
        IAgentToolProvider toolProvider)
        : base(chatClient, configuration, "Author",
            "Assembles all outputs into a RenderManifest for Remotion.",
            AgentType.AuthorAgent, DefaultPrompt,
            toolProvider.GetTools(AgentType.AuthorAgent),
            outputSchemaType: typeof(RenderManifestOutput))
    { }
}
