namespace EasyClothes.App.Viewer;

internal static class Shaders
{
    // Windows renders through ANGLE (OpenGL ES 3.0); Linux uses desktop OpenGL 3.3.
    public static string Header(bool isEs) => isEs
        ? "#version 300 es\nprecision highp float;\n"
        : "#version 330 core\n";

    public const string Vertex = """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aTexCoord;
        uniform mat4 uViewProjection;
        out vec3 vNormal;
        out vec2 vTexCoord;
        void main() {
            vNormal = aNormal;
            vTexCoord = aTexCoord;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    public const string Fragment = """
        in vec3 vNormal;
        in vec2 vTexCoord;
        uniform sampler2D uTexture;
        uniform bool uHasTexture;
        uniform vec3 uLightDir;
        uniform vec4 uFlatColour;
        out vec4 fragColour;
        void main() {
            if (uFlatColour.a > 0.0) { fragColour = uFlatColour; return; }
            vec4 base = uHasTexture ? texture(uTexture, vTexCoord) : vec4(0.72, 0.72, 0.72, 1.0);
            if (base.a < 0.33) discard;
            float diffuse = abs(dot(normalize(vNormal), uLightDir));
            fragColour = vec4(base.rgb * (0.35 + 0.65 * diffuse), 1.0);
        }
        """;
}
