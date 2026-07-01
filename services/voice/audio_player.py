import winsound


class AudioPlayer:

    def __init__(self):

        self.current = None

        self.playing = False

    # ==========================================

    def play(self, wav_file):

        self.stop()

        self.current = str(wav_file)

        self.playing = True

        winsound.PlaySound(
            self.current,
            winsound.SND_FILENAME |
            winsound.SND_ASYNC
        )

    # ==========================================

    def stop(self):

        winsound.PlaySound(
            None,
            winsound.SND_PURGE
        )

        self.playing = False

    # ==========================================

    def is_playing(self):

        return self.playing