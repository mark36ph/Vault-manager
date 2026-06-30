from pathlib import Path
import subprocess


class PiperEngine:

    def __init__(self):

        self.piper_folder = Path("piper")

        self.piper_exe = self.piper_folder / "piper.exe"

        self.espeak_data = self.piper_folder / "espeak-ng-data"

    # =====================================================
    # Checks
    # =====================================================

    def is_installed(self):

        return (
            self.piper_exe.exists()
            and self.espeak_data.exists()
        )

    # =====================================================

    def check_installation(self):

        if not self.piper_exe.exists():

            raise FileNotFoundError(
                "piper.exe not found."
            )

        if not self.espeak_data.exists():

            raise FileNotFoundError(
                "espeak-ng-data folder not found."
            )

    # =====================================================

    def check_voice(self, voice):

        if not voice.model_path.exists():

            raise FileNotFoundError(
                f"Voice model not found:\n{voice.model_path}"
            )

        if not voice.config_path.exists():

            raise FileNotFoundError(
                f"Voice config not found:\n{voice.config_path}"
            )

    # =====================================================
    # Generate Speech
    # =====================================================

    def generate(

        self,

        voice,

        text,

        output_file,

        noise_scale=0.667,

        length_scale=1.0,

        noise_w=0.8,

        sentence_silence=0.2

    ):

        self.check_installation()

        self.check_voice(voice)

        output_file = Path(output_file)

        output_file.parent.mkdir(
            parents=True,
            exist_ok=True
        )

        command = [

            str(self.piper_exe),

            "-m",
            str(voice.model_path),

            "-f",
            str(output_file),

            "--espeak_data",
            str(self.espeak_data),

            "--noise_scale",
            str(noise_scale),

            "--length_scale",
            str(length_scale),

            "--noise_w",
            str(noise_w),

            "--sentence_silence",
            str(sentence_silence)

        ]

        result = subprocess.run(

            command,

            input=text,

            text=True,

            capture_output=True,

            encoding="utf-8"

        )

        if result.returncode != 0:

            raise RuntimeError(

                result.stderr.strip()

            )

        if not output_file.exists():

            raise RuntimeError(

                "Piper did not create the output file."

            )

        return output_file

    # =====================================================
    # Version
    # =====================================================

    def get_version(self):

        try:

            result = subprocess.run(

                [

                    str(self.piper_exe),

                    "--help"

                ],

                capture_output=True,

                text=True

            )

            return result.stdout

        except Exception:

            return None