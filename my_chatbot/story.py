import time
import sys
from google import genai

# Your working Gemini API key
GEMINI_API_KEY = "AIzaSyBlfGAXKQuHSTpvlNGna3PvPM2qlSMfvM4"

print("==========================================")
print("📚 WELCOME TO THE TERMINAL STORY GENERATOR 📚")
print("==========================================\n")

# Initialize the Gemini Client
client = genai.Client(api_key=GEMINI_API_KEY)

# Get story topic from the user
topic = input("👉 What should the story be about? (e.g., A time-traveling football player): ")

if not topic.strip():
    print("Topic cannot be empty! Exiting...")
    sys.exit()

print("\n✨ Generating your story using Gemini AI... Please wait...\n")

# Call the Gemini model with specific instructions for a short story
response = client.models.generate_content(
    model='gemini-2.5-flash',
    contents=f"Write an engaging, creative short story about: {topic}. Keep it around 3 paragraphs."
)

print("================= STORY =================")

# This trick prints the story dramatically, letter-by-letter, like an old typewriter!
for char in response.text:
    sys.stdout.write(char)
    sys.stdout.flush()
    time.sleep(0.01)  # Adjust speed here (lower number = faster)

print("\n=========================================")
print("\n🎉 Story Generation Complete!")