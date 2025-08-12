import server
import json
from types import SimpleNamespace
from threading import Thread
from time import sleep

CONFIG_PATH = './config.json'
CONFIG = None

def main():
    try:
        while True:
            sleep(1)
    except KeyboardInterrupt:
        if server.sock: server.sock.close()
        if server.conn: server.conn.close()


def load_configuration(file_path: str) -> SimpleNamespace:
    with open(file_path, 'r', encoding='utf-8') as f:
        return json.load(f, object_hook=lambda x: SimpleNamespace(**x))

if __name__ == '__main__':
    CONFIG = load_configuration(CONFIG_PATH)
    server_thread = Thread(target=server.main, args=(CONFIG,), daemon=True)
    server_thread.start()
    main()
