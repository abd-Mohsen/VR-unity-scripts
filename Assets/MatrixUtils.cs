// using UnityEngine;
// using System;
// using System.Collections.Generic;
// using static LoadModels;

// public static class MatrixUtils
// {
//     public static string ConvertMatrixToJson(Matrix4x4 matrix)
//     {
//         var matrixData = new MatrixData
//         {
//             rows = new List<MatrixRow>()
//         };

//         for (int i = 0; i < 4; i++)
//         {
//             matrixData.rows.Add(new MatrixRow
//             {
//                 values = new List<float>
//                 {
//                     matrix[i, 0],
//                     matrix[i, 1],
//                     matrix[i, 2],
//                     matrix[i, 3]
//                 }
//             });
//         }

//         return JsonUtility.ToJson(matrixData);
//     }
// }