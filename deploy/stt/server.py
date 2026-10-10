"""Speech-to-text service for the Debits bot: one Whisper model behind a tiny HTTP API.

POST /transcribe?language=uz&prompt=...&name=voice.ogg   body = the audio file bytes (ogg/opus, webm, mp3, m4a, wav)
    -> {"text": "...", "language": "uz", "duration": 4.2, "elapsed": 1.7}
GET  /health  -> 200 {"status": "ok", "model": "..."} once the model is loaded, 503 while it is loading

Only the standard library and faster-whisper are used. Inference is serialised: a CPU model gains nothing from parallel runs.
"""

import io
import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

MODEL_NAME = os.environ.get("STT_MODEL", "yuldashev222/uzbek_stt_v1-ct2")
COMPUTE_TYPE = os.environ.get("STT_COMPUTE_TYPE", "int8")
THREADS = int(os.environ.get("STT_THREADS", "0") or 0)  # 0 = let CTranslate2 decide
PORT = int(os.environ.get("STT_PORT", "8000"))
MAX_BYTES = 12 * 1024 * 1024

model = None
model_error = None
inference = threading.Lock()


def load_model():
    global model, model_error
    try:
        from faster_whisper import WhisperModel

        started = time.time()
        print(f"loading {MODEL_NAME} ({COMPUTE_TYPE}); the first start downloads it", flush=True)
        model = WhisperModel(MODEL_NAME, device="cpu", compute_type=COMPUTE_TYPE, cpu_threads=THREADS)
        print(f"model ready in {time.time() - started:.1f}s", flush=True)
    except Exception as error:  # keep the process alive so /health can say what is wrong
        model_error = str(error)
        print(f"model failed to load: {error}", flush=True)


class Handler(BaseHTTPRequestHandler):
    server_version = "debits-stt"

    def reply(self, status, payload):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if urlparse(self.path).path != "/health":
            return self.reply(404, {"error": "not found"})
        if model is not None:
            return self.reply(200, {"status": "ok", "model": MODEL_NAME})
        self.reply(503, {"status": "error" if model_error else "loading", "model": MODEL_NAME, "error": model_error})

    def do_POST(self):
        url = urlparse(self.path)
        if url.path != "/transcribe":
            return self.reply(404, {"error": "not found"})
        if model is None:
            return self.reply(503, {"error": model_error or "model is loading"})

        length = int(self.headers.get("Content-Length", "0") or 0)
        if length <= 0 or length > MAX_BYTES:
            return self.reply(413 if length > MAX_BYTES else 400, {"error": "audio body of 1 byte .. 12 MB is required"})
        audio = self.rfile.read(length)

        query = parse_qs(url.query)
        language = (query.get("language", ["uz"])[0] or "uz").lower()
        prompt = query.get("prompt", [""])[0] or None

        started = time.time()
        try:
            with inference:
                segments, info = model.transcribe(
                    io.BytesIO(audio),
                    language=language,
                    initial_prompt=prompt,
                    beam_size=5,
                    temperature=0.0,
                    condition_on_previous_text=False,
                    vad_filter=True,
                )
                text = " ".join(segment.text.strip() for segment in segments).strip()
        except Exception as error:
            print(f"transcription failed: {error}", flush=True)
            return self.reply(422, {"error": f"cannot transcribe: {error}"})

        elapsed = time.time() - started
        print(f"{length} bytes, audio {info.duration:.1f}s, took {elapsed:.1f}s: {text}", flush=True)
        self.reply(200, {"text": text, "language": info.language, "duration": round(info.duration, 2), "elapsed": round(elapsed, 2)})

    def log_message(self, *args):  # our own one-line logs are enough
        pass


if __name__ == "__main__":
    threading.Thread(target=load_model, daemon=True).start()
    print(f"listening on :{PORT}", flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
