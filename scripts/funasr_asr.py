#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import argparse
import json
import os
import re
import sys
import traceback
from typing import Any, Dict, List

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


def _postprocess_text(text: str) -> str:
    if not text:
        return ""

    out = str(text)
    # SenseVoice tags, e.g. <|zh|><|NEUTRAL|><|Speech|><|woitn|>
    out = re.sub(r"<\|[^|>]+\|>", "", out)
    out = re.sub(r"\s+", " ", out).strip()

    try:
        from funasr.utils.postprocess_utils import rich_transcription_postprocess  # type: ignore

        out = rich_transcription_postprocess(out)
    except Exception:
        pass

    return out.strip()


def _extract_text_and_conf(result: Any) -> Dict[str, Any]:
    text = ""
    confidence = 0.65

    if isinstance(result, dict):
        text = _postprocess_text(str(result.get("text") or result.get("sentence") or "").strip())
        conf = result.get("confidence") or result.get("score")
        if isinstance(conf, (int, float)):
            confidence = float(conf)
        return {"text": text, "confidence": max(0.0, min(1.0, confidence))}

    if isinstance(result, list):
        texts: List[str] = []
        confs: List[float] = []
        for item in result:
            if isinstance(item, dict):
                t = _postprocess_text(str(item.get("text") or item.get("sentence") or "").strip())
                if t:
                    texts.append(t)
                c = item.get("confidence") or item.get("score")
                if isinstance(c, (int, float)):
                    confs.append(float(c))

        text = " ".join(texts).strip()
        if confs:
            confidence = sum(confs) / len(confs)
        return {"text": text, "confidence": max(0.0, min(1.0, confidence))}

    return {"text": "", "confidence": 0.0}


def _create_model(model_name: str, device: str):
    from funasr import AutoModel  # type: ignore

    try:
        return AutoModel(model=model_name, device=device, trust_remote_code=True)
    except TypeError:
        return AutoModel(model=model_name, device=device)


def _recognize_with_model(model: Any, wav_path: str) -> Dict[str, Any]:
    if not os.path.exists(wav_path):
        return {"ok": False, "error": f"wav not found: {wav_path}"}

    try:
        try:
            result = model.generate(input=wav_path, language="zh", use_itn=True)
        except TypeError:
            result = model.generate(input=wav_path)
        parsed = _extract_text_and_conf(result)
        return {
            "ok": True,
            "text": parsed["text"],
            "confidence": parsed["confidence"],
        }
    except Exception as ex:
        first_err = f"{type(ex).__name__}: {ex}"
        # Fallback: some environments fail when passing file path directly.
        # Retry with in-memory waveform.
        try:
            import soundfile as sf  # type: ignore

            audio, sr = sf.read(wav_path, dtype="float32")
            if hasattr(audio, "ndim") and audio.ndim > 1:
                audio = audio.mean(axis=1)

            try:
                result = model.generate(input=audio, fs=int(sr), language="zh", use_itn=True)
            except TypeError:
                result = model.generate(input=audio)

            parsed = _extract_text_and_conf(result)
            return {
                "ok": True,
                "text": parsed["text"],
                "confidence": parsed["confidence"],
            }
        except Exception as ex2:
            second_err = f"{type(ex2).__name__}: {ex2}"
            tb = traceback.format_exc(limit=1).strip().replace("\r", " ").replace("\n", " | ")
            return {
                "ok": False,
                "error": f"inference failed: path={first_err}; array={second_err}; tb={tb}",
            }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--wav", default="")
    parser.add_argument("--model", default="iic/SenseVoiceSmall")
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--server", action="store_true")
    args = parser.parse_args()

    if not args.server:
        if not args.wav:
            print(json.dumps({"ok": False, "error": "wav argument is required in non-server mode"}))
            return 2
        if not os.path.exists(args.wav):
            print(json.dumps({"ok": False, "error": f"wav not found: {args.wav}"}))
            return 2

    try:
        model = _create_model(args.model, args.device)
    except Exception as ex:
        print(json.dumps({"ok": False, "error": f"import funasr failed: {ex}"}))
        return 3

    if args.server:
        print(json.dumps({"ok": True, "event": "ready"}), flush=True)
        for line in sys.stdin:
            payload = (line or "").strip()
            if not payload:
                continue

            try:
                req = json.loads(payload)
            except Exception as ex:
                print(json.dumps({"ok": False, "error": f"bad-request: {ex}"}), flush=True)
                continue

            if req.get("cmd") == "shutdown":
                print(json.dumps({"ok": True, "event": "bye"}), flush=True)
                return 0

            wav_path = str(req.get("wav") or "").strip()
            out = _recognize_with_model(model, wav_path)
            print(json.dumps(out), flush=True)

        return 0

    out = _recognize_with_model(model, args.wav)
    print(json.dumps(out))
    if out.get("ok"):
        return 0
    return 4


if __name__ == "__main__":
    sys.exit(main())
