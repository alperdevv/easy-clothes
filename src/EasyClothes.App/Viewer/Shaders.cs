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
            vec3 n = normalize(vNormal);
            float diffuse = abs(dot(n, uLightDir));
            float sky = abs(n.z) * 0.5 + 0.5;
            fragColour = vec4(base.rgb * (0.28 + 0.62 * diffuse + 0.12 * sky), 1.0);
        }
        """;

    // Full-screen triangle from gl_VertexID, so the background needs no vertex buffer.
    public const string BackgroundVertex = """
        out vec2 vUv;
        void main() {
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 1.0, 1.0);
        }
        """;

    // Photo studio backdrop: light grey at the top fading to dark, a soft glow behind the subject and a vignette.
    public const string BackgroundFragment = """
        in vec2 vUv;
        uniform float uAspect;
        out vec4 fragColour;
        void main() {
            vec3 top = vec3(0.47, 0.49, 0.53);
            vec3 bottom = vec3(0.13, 0.135, 0.15);
            vec3 colour = mix(bottom, top, smoothstep(0.0, 1.0, vUv.y));
            vec2 d = (vUv - vec2(0.5, 0.55)) * vec2(uAspect, 1.0);
            colour += vec3(0.07) * exp(-dot(d, d) * 3.0);
            vec2 v = vUv - 0.5;
            colour *= 1.0 - 0.55 * dot(v, v);
            fragColour = vec4(colour, 1.0);
        }
        """;

    // Soft contact shadow: a quad on the floor under the scene, darkest in the middle.
    public const string ShadowVertex = """
        uniform mat4 uViewProjection;
        uniform vec3 uCentre;
        uniform vec2 uRadius;
        out vec2 vOffset;
        void main() {
            vec2 corner = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1)) * 2.0 - 1.0;
            vOffset = corner;
            gl_Position = uViewProjection * vec4(uCentre + vec3(corner * uRadius, 0.0), 1.0);
        }
        """;

    public const string ShadowFragment = """
        in vec2 vOffset;
        out vec4 fragColour;
        void main() {
            float r = length(vOffset);
            float alpha = 0.5 * pow(1.0 - smoothstep(0.0, 1.0, r), 1.6);
            fragColour = vec4(0.0, 0.0, 0.0, alpha);
        }
        """;
}
