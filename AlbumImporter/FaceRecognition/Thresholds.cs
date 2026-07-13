using Bitmanager.Core;
using Bitmanager.Xml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace AlbumImporter.FaceRecognition {
   public class FaceRecognitionThresholds {
      public readonly float ScoreThreshold;
      public readonly float MinScoreDiff;
      public readonly float MaxScoreDiffFactor;

      public FaceRecognitionThresholds(XmlNode node) {
         ScoreThreshold = (float)node.ReadFloat ("@score", 0.25f);
         MinScoreDiff = (float)node.ReadFloat ("@min_diff", 0.03f);
         MaxScoreDiffFactor = (float)node.ReadFloat ("@max_diff_factor", 0.9f);
      }

      public override string ToString() {
         return Invariant.Format ("FaceThresholds [score={0:F2} min_diff={0:F2}, max_diff_factor={2:F2}]", ScoreThreshold, MinScoreDiff, MaxScoreDiffFactor);
      }
   }
}
