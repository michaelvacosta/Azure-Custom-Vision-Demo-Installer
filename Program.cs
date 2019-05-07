using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO.Compression;

namespace ImageClassification
{
    class Program
    {
        private const string SouthCentralUsEndpoint = "https://southcentralus.api.cognitive.microsoft.com";

        static void Main(string[] args)
        {
            // You can either add your training key from the settings page of the portal here, pass it on the command line, or type it in when the program runs
            string trainingKey = GetTrainingKey("<your training key here>", args);

            // Create the Api, passing in the training key
            CustomVisionTrainingClient trainingApi = new CustomVisionTrainingClient()
            {
                ApiKey = trainingKey,
                Endpoint = SouthCentralUsEndpoint
            };

            // Get path to zip file containing images
            string zipFilePath = GetZipFilePath("<your zip file path here>", args);
            try
            {
                // Normalize the path
                zipFilePath = Path.GetFullPath(zipFilePath);
            }
            catch
            {
                // Update status for user and close out
                ErrorHandler("Invalid path - please make sure to provide a valid path to a zip file. Press any key to exit\n");
            }

            string newProjectName="";
            try
            {
                // Get name of zip file (without extension) - this will become the name of the Custom Vision project
                string zipFileName = Path.GetFileNameWithoutExtension(zipFilePath);
                newProjectName = zipFileName;
            }
            catch
            {
                // Update status for user and close out
                ErrorHandler("Invalid path or file - please make sure to provide a valid path to a zip file. Press any key to exit\n");
            }

            try
            {
                // Create a new project using the name of the zip file
                var projectNew = trainingApi.CreateProject(newProjectName);

                // Update status for user
                Console.WriteLine("Created new project: " + newProjectName);

                try
                {
                    using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                    {
                        string tagName = "";
                        Guid imageTagId;
                        var tagNames = new Dictionary<string, Guid>();

                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                            {
                                // Take the tag name from the folder that each image is in
                                tagName = Path.GetDirectoryName(entry.FullName);

                                // If we have seen this tag before, simply use the ID previously generated so we can avoid errors related to duplication of tags in the model
                                if (tagNames.TryGetValue(tagName, out imageTagId)) { }
                                else
                                {
                                    try
                                    {
                                        // If we have NOT seen this tag before, create it in the model now
                                        var newTag = trainingApi.CreateTag(projectNew.Id, tagName);

                                        // Update status for user
                                        Console.WriteLine("Created new tag: " + tagName);

                                        // Add info for this tag to our dictionary which keeps track of tag names and IDs
                                        imageTagId = newTag.Id;
                                        tagNames.Add(tagName, imageTagId);
                                    }
                                    catch
                                    {
                                        // Update status for user
                                        ErrorHandler("Unable to create new tag: " + tagName);
                                    }
                                }

                                // Images will be uploaded one at a time as they are read out of the zip file
                                using (var stream = entry.Open())
                                {
                                    try
                                    {
                                        trainingApi.CreateImagesFromData(projectNew.Id, stream, new List<string>() { imageTagId.ToString() });

                                        // Update status for user
                                        Console.WriteLine("Uploaded new image: " + entry.Name);
                                    }
                                    catch
                                    {
                                        // Update status for user
                                        ErrorHandler("Unable to upload image: " + entry.Name);
                                    }
                                }
                            }
                        }

                        // Update status for user
                        Console.WriteLine("Training new project: " + newProjectName);

                        try
                        {
                            // Now that there are images with tags, train the new project
                            var iterationNew = trainingApi.TrainProject(projectNew.Id);

                            // The returned iteration will be in progress, and can be queried periodically to see when it has completed
                            while (iterationNew.Status == "Training")
                            {
                                // Wait for another second
                                Thread.Sleep(1000);

                                // Re-query the iteration to get it's updated status
                                iterationNew = trainingApi.GetIteration(projectNew.Id, iterationNew.Id);
                            }

                            // The iteration is now trained. Make it the default project endpoint
                            iterationNew.IsDefault = true;
                            trainingApi.UpdateIteration(projectNew.Id, iterationNew.Id, iterationNew);
                        }
                        catch
                        {
                            // Update status for user
                            ErrorHandler("Unable to train project, please train manually.");
                        }

                        try
                        {
                            // You can either add your prediction key from the settings page of the portal here, pass it on the command line, or type it in when the program runs
                            string predictionKey = GetPredictionKey("<your prediction key here>", args);

                            // You can either add your test image path here, pass it on the command line, or type it in when the program runs
                            string testImageFilePath = GetTestImagePath("<your test image file path here>", args);

                            // You can either add your prediction URL from the settings page of the portal here, pass it on the command line, or type it in when the program runs
                            string predictionURL = GetPredictionURL("<your prediction URL here>", args);

                            MakePredictionRequest(predictionKey, testImageFilePath, predictionURL).Wait();
                        }
                        catch
                        {
                            // Update status for user
                            ErrorHandler("Unable to obtain prediction, please try again manually.");
                        }


                        // Update status for user and close out
                        Console.WriteLine("Done! Press any key to exit\n");
                        Console.ReadKey();
                    }
                }
                catch
                {
                    // Update status for user and close out
                    ErrorHandler("Invalid path or file - please make sure to provide a valid path to a zip file. Press any key to exit\n");
                }
            }
            catch
            {
                // Update status for user and close out
                ErrorHandler("Unable to create project - please make sure to provide a valid Custom Vision Service key and a valid zip file containing images to use with the service. Press any key to exit\n");
            }
        }

        private static void ErrorHandler(string userMessage)
        {
            // Update status for user and close out
            Console.WriteLine(userMessage);
            Console.ReadKey();
            Environment.Exit(0);
        }

        private static string GetTrainingKey(string trainingKey, string[] args)
        {
            if (string.IsNullOrWhiteSpace(trainingKey) || trainingKey.Equals("<your training key here>"))
            {
                if (args.Length >= 1)
                {
                    trainingKey = args[0];
                }

                while (string.IsNullOrWhiteSpace(trainingKey) || trainingKey.Length != 32)
                {
                    Console.Write("Enter your training key: ");
                    trainingKey = Console.ReadLine();
                }
                Console.WriteLine();
            }

            return trainingKey;
        }

        private static string GetZipFilePath(string zipFilePath, string[] args)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || zipFilePath.Equals("<your zip file path here>"))
            {
                if (args.Length >= 2)
                {
                    zipFilePath = args[1];
                }

                while (string.IsNullOrWhiteSpace(zipFilePath) || zipFilePath.Equals("<your zip file path here>"))
                {
                    Console.Write("Enter the full path to the zip file containing your Custom Vision assets: ");
                    zipFilePath = Console.ReadLine();
                }
                Console.WriteLine();
            }

            return zipFilePath;
        }

        private static string GetPredictionKey(string predictionKey, string[] args)
        {
            if (string.IsNullOrWhiteSpace(predictionKey) || predictionKey.Equals("<your prediction key here>"))
            {
                if (args.Length >= 3)
                {
                    predictionKey = args[2];
                }

                while (string.IsNullOrWhiteSpace(predictionKey) || predictionKey.Length != 32)
                {
                    Console.Write("Enter your prediction key: ");
                    predictionKey = Console.ReadLine();
                }
                Console.WriteLine();
            }

            return predictionKey;
        }

        private static string GetTestImagePath(string testImagePath, string[] args)
        {
            if (string.IsNullOrWhiteSpace(testImagePath) || testImagePath.Equals("<your test image file path here>"))
            {
                if (args.Length >= 4)
                {
                    testImagePath = args[3];
                }

                while (string.IsNullOrWhiteSpace(testImagePath) || testImagePath.Equals("<your test image file path here>"))
                {
                    Console.Write("Enter the full path to the image to test with your Custom Vision Service: ");
                    testImagePath = Console.ReadLine();
                }
                Console.WriteLine();
            }

            return testImagePath;
        }

        private static string GetPredictionURL(string predictionURL, string[] args)
        {
            if (string.IsNullOrWhiteSpace(predictionURL) || predictionURL.Equals("<your prediction URL here>"))
            {
                if (args.Length >= 5)
                {
                    predictionURL = args[4];
                }

                while (string.IsNullOrWhiteSpace(predictionURL) || predictionURL.Equals("<your prediction URL here>"))
                {
                    Console.Write("Enter the prediction URL from your trained Custom Vision Service model here: ");
                    predictionURL = Console.ReadLine();
                }
                Console.WriteLine();
            }

            return predictionURL;
        }

        private static async Task MakePredictionRequest(string predictionKey, string imageFilePath, string predictionURL)
        {
            var client = new HttpClient();

            // Request headers - replace this example key with your valid Prediction-Key.
            client.DefaultRequestHeaders.Add("Prediction-Key", predictionKey);

            HttpResponseMessage response;

            // Request body. Try this sample with a locally stored image.
            byte[] byteData = GetImageAsByteArray(imageFilePath);

            using (var content = new ByteArrayContent(byteData))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response = await client.PostAsync(predictionURL, content);
                Console.WriteLine(await response.Content.ReadAsStringAsync());
            }
        }

        private static byte[] GetImageAsByteArray(string imageFilePath)
        {
            FileStream fileStream = new FileStream(imageFilePath, FileMode.Open, FileAccess.Read);
            BinaryReader binaryReader = new BinaryReader(fileStream);
            return binaryReader.ReadBytes((int)fileStream.Length);
        }

    }
}
