import streamlit as st
from google import genai

# Configure webpage appearance safely without emojis
st.set_page_config(page_title="My AI Chatbot", page_icon="🤖")
st.title("My Personal AI Assistant")

# Your working API key
GEMINI_API_KEY = "AIzaSyBlfGAXKQuHSTpvlNGna3PvPM2qlSMfvM4"

# Connect to Gemini Engine safely
@st.cache_resource
def get_ai_client():
    return genai.Client(api_key=GEMINI_API_KEY)

client = get_ai_client()

# Initialize chat history memory on the webpage
if "messages" not in st.session_state:
    st.session_state.messages = []

# Display previous conversation messages layout
for message in st.session_state.messages:
    with st.chat_message(message["role"]):
        st.write(message["content"])

# User input field
if user_query := st.chat_input("Type your message here..."):
    # Display user message
    with st.chat_message("user"):
        st.write(user_query)
    st.session_state.messages.append({"role": "user", "content": user_query})
    
    # Generate response from Gemini
    with st.chat_message("assistant"):
        with st.spinner("Thinking..."):
            response = client.models.generate_content(
                model='gemini-2.5-flash',
                contents=user_query
            )
            st.write(response.text)
    st.session_state.messages.append({"role": "assistant", "content": response.text})