namespace SharedJobLibrary.Services.TrendAnalysis
{
    public class SimpleTrends
	{
		private const int _seriesSize = 3;
		private long[] _trendSeries = new long[_seriesSize];

		public void UpdateSeries(long value)
		{
			// left shift position for all values, add new item to the end
			for (int row = 0; row < _trendSeries.GetUpperBound(0); row++)
			{
				_trendSeries[row] = _trendSeries[row + 1];
			}
			_trendSeries[_trendSeries.GetUpperBound(0)] = value;
		}

		private static long SumOfSeriesSize(int N)
		{
			long sum = 0L;

			for (int i = 1; i <= N; i++)
			{
				sum += i;
			}

			return sum;
		}

		private static long SumOfSeriesValue(long[] N)
		{
			long sum = 0L;

			for (int i = 0; i <= N.GetUpperBound(0); i++)
			{
				sum += N[i];
			}

			return sum;
		}

		private static long SumOfSamplePositionTimesSeriesValue(int numSamples, long[] N)
		{
			long sum = 0L;

			for (int i = 1; i <= numSamples; i++)
			{
				sum += (N[i - 1] * i);
			}

			return sum;
		}

		private static long SumOfSamplePositionSquared(long N)
		{
			long sum = 0L;

			for (int i = 1; i <= N; i++)
			{
				sum += (i * i);
			}

			return sum;
		}

		public double CalculateTrend()
		{
			long sampleTotal = SumOfSeriesSize(_seriesSize);
			long valuesTotal = SumOfSeriesValue(_trendSeries);
			long sampleTimesValueTotal = SumOfSamplePositionTimesSeriesValue(_seriesSize, _trendSeries);
			long sampleSquaredTotal = SumOfSamplePositionSquared(_seriesSize);

			return ((_seriesSize * sampleTimesValueTotal) - (sampleTotal * valuesTotal)) / ((_seriesSize * sampleSquaredTotal) - (sampleTotal * sampleTotal));
		}

		//public static void Smooth(this List<Vector2> pointList)
		//{
		//	List<Vector2> smoothedPoints = new List<Vector2>();

		//	for (int i = 1; i < pointList.Count; i++)
		//	{
		//		if (Vector2.Distance(pointList[i - 1], pointList[i]) < 30f)
		//		{
		//			pointList.RemoveAt(i);
		//			i--;
		//		}
		//	}

		//	if (pointList.Count < 4) return;

		//	smoothedPoints.Add(pointList[0]);

		//	for (int i = 1; i < pointList.Count - 2; i++)
		//	{
		//		smoothedPoints.Add(pointList[i]);

		//		smoothedPoints.Add(Vector2.CatmullRom(pointList[i - 1], pointList[i], pointList[i + 1], pointList[i + 2], .5f));
		//		//smoothedPoints.Add(Vector2.CatmullRom(pointList[i - 1], pointList[i], pointList[i + 1], pointList[i + 2], .2f));
		//		//smoothedPoints.Add(Vector2.CatmullRom(pointList[i - 1], pointList[i], pointList[i + 1], pointList[i + 2], .3f));
		//		//smoothedPoints.Add(Vector2.CatmullRom(pointList[i - 1], pointList[i], pointList[i + 1], pointList[i + 2], .7f));
		//		//smoothedPoints.Add(Vector2.CatmullRom(pointList[i - 1], pointList[i], pointList[i + 1], pointList[i + 2], .8f));
		//		//smoothedPoints.Add(Vector2.CatmullRom(pointList[i - 1], pointList[i], pointList[i + 1], pointList[i + 2], .9f));
		//	}

		//	smoothedPoints.Add(pointList[pointList.Count - 2]);
		//	smoothedPoints.Add(pointList[pointList.Count - 1]);

		//	pointList.Clear();
		//	pointList.AddRange(smoothedPoints);
		//}
	}
}
