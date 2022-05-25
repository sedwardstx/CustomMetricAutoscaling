using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SharedJobLibrary.Services.TrendAnalysis
{
    public class Mandelbrot
    {
        private double yMin = -100.0;                                 // Default minimum Y for the set to render.
        private double yMax = 50.0;                                  // Default maximum Y for the set to render.
        private double xMin = -100.0;                                 // Default minimum X for the set to render.
        private double xMax = 50.0;                                  // Default maximum X for the set to render.
        private int kMax = 50;                                      // Default maximum number of iterations for Mandelbrot calculation.


        public Task<double> Compute(double imageHeight, CancellationToken cancellationToken)
        {
            double modulusSquared;

            yMin = -imageHeight;                                 // Default minimum Y for the set to render.
            yMax = imageHeight;                                  // Default maximum Y for the set to render.
            xMin = -imageHeight;                                 // Default minimum X for the set to render.
            xMax = imageHeight;                                  // Default maximum X for the set to render.

            // This increment is converted to mathematical coordinates.
            double xyPixelStep = .01;
            ComplexPoint pixelStep = new ComplexPoint(xyPixelStep, xyPixelStep);

            // Start stopwatch - used to measure performance improvements
            // (from improving the efficiency of the maths implementation).
            Stopwatch sw = new Stopwatch();
            sw.Start();

            // Main loop, nested over Y (outer) and X (inner) values.
            int lineNumber = 0;
            double yPix = imageHeight - 1;
            for (double y = yMin; y < yMax; y += xyPixelStep)
            {
                double xPix = 0;
                for (double x = xMin; x < xMax; x += xyPixelStep)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Create complex point C = x + i*y.
                    ComplexPoint c = new ComplexPoint(x, y);

                    // Initialise complex value Zk.
                    ComplexPoint zk = new ComplexPoint(0, 0);

                    // Do the main Mandelbrot calculation. Iterate until the equation
                    // converges or the maximum number of iterations is reached.
                    int k = 0;
                    do
                    {
                        zk = zk.doCmplxSqPlusConst(c);
                        modulusSquared = zk.doMoulusSq();
                        k++;
                    } while ((modulusSquared <= 4.0) && (k < kMax));


                    xPix += xyPixelStep;
                }
                yPix -= xyPixelStep;
                lineNumber++;

            }
            // Finished rendering. Stop the stopwatch and show the elapsed time.
            sw.Stop();

            return Task.FromResult(sw.Elapsed.TotalSeconds);
        }
    }
}
