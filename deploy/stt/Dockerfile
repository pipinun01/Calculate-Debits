# Optional self-hosted speech-to-text for Uzbek (faster-whisper on CPU). Enabled with COMPOSE_PROFILES=stt.
# The model is downloaded from Hugging Face on first start into the stt-models volume (about 3 GB for the default one).
FROM python:3.12-slim
ENV PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    HF_HOME=/models \
    HF_HUB_DISABLE_TELEMETRY=1
WORKDIR /srv
COPY requirements.txt .
RUN pip install -r requirements.txt
COPY server.py .
RUN useradd --system --uid 10001 stt && mkdir /models && chown stt /models
USER stt
VOLUME /models
EXPOSE 8000
HEALTHCHECK --interval=30s --timeout=5s --start-period=600s --retries=3 \
    CMD python -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://localhost:8000/health', timeout=4).status == 200 else 1)"
CMD ["python", "server.py"]
