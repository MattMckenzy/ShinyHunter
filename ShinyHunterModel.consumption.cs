// This file was auto-generated by ML.NET Model Builder. 
using Microsoft.Extensions.ObjectPool;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.Collections.Concurrent;

namespace ShinyHunter
{
    public partial class ShinyHunterModel
    {
        /// <summary>
        /// model input class for ShinyHunterModel.
        /// </summary>
        #region model input class
        public class ModelInput
        {
            [ColumnName(@"Label")]
            public string Label { get; set; }

            [ColumnName(@"ImageSource")]
            public string ImageSource { get; set; }

        }

        #endregion

        /// <summary>
        /// model output class for ShinyHunterModel.
        /// </summary>
        #region model output class
        public class ModelOutput
        {
            [ColumnName("PredictedLabel")]
            public string Prediction { get; set; }

            public float[] Score { get; set; }
        }

        #endregion

        private static string MLNetModelPath = Path.GetFullPath("ShinyHunterModel.zip");

        private static readonly MLContext MLContext = new();
        private static readonly ITransformer MLModel = MLContext.Model.Load(MLNetModelPath, out DataViewSchema _);
        private static ObjectPool<PredictionEngine<ModelInput, ModelOutput>> PredictionEnginePool = null;

        public static void CreateAndFillPool(int poolSize = 0)
        {
            poolSize = poolSize == 0 ? Environment.ProcessorCount : poolSize;

            PredictionEnginePool = new DefaultObjectPool<PredictionEngine<ModelInput, ModelOutput>>(new PooledPredictionEnginePolicy(MLContext, MLModel), poolSize);            

            Stack<PredictionEngine<ModelInput, ModelOutput>> predictionEngines = new();
            for (int i = 0; i < poolSize; i++)
            {
                predictionEngines.Push(PredictionEnginePool.Get());
            }

            for (int i = 0; i < poolSize; i++)
            {
                PredictionEnginePool.Return(predictionEngines.Pop());
            }
        }

        public static ModelOutput Predict(ModelInput modelInput)
        {
            PredictionEngine<ModelInput, ModelOutput> predictionEngine = PredictionEnginePool.Get();
            ModelOutput result = predictionEngine.Predict(modelInput);
            PredictionEnginePool.Return(predictionEngine);
            return result;
        }
        
        private class PooledPredictionEnginePolicy : IPooledObjectPolicy<PredictionEngine<ModelInput, ModelOutput>>
        {
            private readonly MLContext _mlContext;
            private readonly ITransformer _model;

            public PooledPredictionEnginePolicy(MLContext mlContext, ITransformer model)
            {
                _mlContext = mlContext;
                _model = model;
            }

            public PredictionEngine<ModelInput, ModelOutput> Create() =>
                _mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(_model);            

            public bool Return(PredictionEngine<ModelInput, ModelOutput> predictionEngine)
            {
                if (predictionEngine == null)
                    return false;

                return true;
            }
        }

    }
}
