using System.Numerics;
namespace Glib;

public struct Matrix3x3
{
    public float M11;
    public float M12;
    public float M13;
    public float M21;
    public float M22;
    public float M23;
    public float M31;
    public float M32;
    public float M33;

    public Matrix3x3(Matrix4x4 srcMatrix)
    {
        M11 = srcMatrix.M11;
        M12 = srcMatrix.M12;
        M13 = srcMatrix.M13;
        M21 = srcMatrix.M21;
        M22 = srcMatrix.M22;
        M23 = srcMatrix.M23;
        M31 = srcMatrix.M31;
        M32 = srcMatrix.M32;
        M33 = srcMatrix.M33;
    }

    public static explicit operator Matrix4x4(Matrix3x3 mat)
    {
        return new Matrix4x4(
            mat.M11, mat.M12, mat.M13, 0f,
            mat.M21, mat.M22, mat.M23, 0f,
            mat.M31, mat.M32, mat.M33, 0f,
            0,       0,       0,       1f
        );
    }
}

public static class GlibMath
{
    public static Matrix4x4 CreatePerspective(float fovRadians, float aspect, float near, float far)
    {
        float f = MathF.Tan(MathF.PI * 0.5f - 0.5f * fovRadians);
        float rangeInv = 1.0f / (near - far);

        return new Matrix4x4(
            f / aspect, 0f, 0f, 0f,
            0f, f, 0f, 0f,
            0f, 0f, (near + far) * rangeInv, -1f,
            0f, 0f, near * far * rangeInv * 2f, 0f    
        );
    }
}