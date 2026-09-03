using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AutoCAD_Robot_simulation
{
    public class AI
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent";

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
        private static string _cachedApiKey;

        private static string GetApiKey()
        {
            if (_cachedApiKey != null) return _cachedApiKey;

            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string configPath = Path.Combine(dllDir, "appsettings.json");

            if (!File.Exists(configPath))
                throw new FileNotFoundException($"API key configuration file not found at: {configPath}");

            var config = JObject.Parse(File.ReadAllText(configPath));
            string key = config["GeminiApiKey"]?.ToString();

            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("appsettings.json is missing or has an empty GeminiApiKey.");

            _cachedApiKey = key;
            return _cachedApiKey;
        }

        public static async Task<RobotTask> GetCoordinatesFromTextAsync(string commandText)
        {
            string endpoint = $"{BaseUrl}?key={GetApiKey()}";

            string finalPrompt = $@"You are a spatial coordinate parser for an industrial robot arm. 
The input text is transcribed from an offline speech-to-text engine and may contain phonetic errors (e.g., 'egg' instead of 'X', 'why' instead of 'Y', 'fifteen' instead of 'fifty', 'to' instead of 'two'). 
Please infer the correct spatial coordinates based on context and fix any phonetic mistakes.

CRITICAL LIMITATION: The maximum reach of this robot is 280 units. If the user requests a coordinate greater than 280 or less than -280, force that axis value to 0.
RETURN ONLY A JSON STRING in this exact format: {{""Pick"": {{""X"": 0, ""Y"": 100, ""Z"": 0}}, ""Place"": {{""X"": 0, ""Y"": 200, ""Z"": 0}}}}. 
Default unmentioned axes to 0.

User command: '{commandText}'";

            var requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = finalPrompt } } } }
            };

            string jsonContent = JsonConvert.SerializeObject(requestBody);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);
            string responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Gemini API Error (Code {response.StatusCode}): {responseString}");

            JObject jsonResponse = JObject.Parse(responseString);
            string aiText = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();

            if (string.IsNullOrWhiteSpace(aiText))
                throw new InvalidOperationException("Gemini returned empty content. Cannot extract coordinates.");

            aiText = aiText.Replace("```json", "").Replace("```", "").Trim();

            var result = JsonConvert.DeserializeObject<RobotTask>(aiText);

            if (result?.Pick == null || result.Place == null)
                throw new InvalidOperationException("AI returned invalid coordinate data or is missing Pick/Place fields.");

            return result;
        }
    }

    public class RobotTask
    {
        public Coordinate Pick { get; set; }
        public Coordinate Place { get; set; }
    }

    public class Coordinate
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}