namespace QuickNotchCs;

/// <summary>Порт spring.py: пружина с подшагами по 4 мс.</summary>
public sealed class Spring
{
    public double X, V, Target;

    public Spring(double x = 0.0)
    {
        X = x;
        Target = x;
    }

    public void Step(double dt, double k, double c)
    {
        if (Settled)
        {
            if (X != Target || V != 0.0) Snap();
            return;
        }
        int n = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            V += (k * (Target - X) - c * V) * h;
            X += V * h;
        }
    }

    public bool Settled => Math.Abs(Target - X) < 1e-3 && Math.Abs(V) < 0.01;

    public void Snap()
    {
        X = Target;
        V = 0.0;
    }

    public static double EaseOutBack(double x, double s = 1.2)
    {
        double c3 = s + 1;
        return 1 + c3 * Math.Pow(x - 1, 3) + s * Math.Pow(x - 1, 2);
    }
}

