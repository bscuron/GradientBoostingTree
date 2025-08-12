import server

HISTORICAL_DATA = []
REALTIME_DATA = []

def handle(payload: dict):
    global HISTORICAL_DATA, REALTIME_DATA
    PAYLOAD_TYPE = server.PAYLOAD_TYPE
    match payload.get('type'):
        case PAYLOAD_TYPE.PING: return
        case PAYLOAD_TYPE.HISTORICAL_BEGIN:
            HISTORICAL_DATA = []
        case PAYLOAD_TYPE.HISTORICAL_DATA:
            HISTORICAL_DATA.append(payload)
        case PAYLOAD_TYPE.HISTORICAL_END:
            # TODO: train the model on historical data
            return
        case PAYLOAD_TYPE.REALTIME_BEGIN:
            REALTIME_DATA = []
        case PAYLOAD_TYPE.REALTIME_DATA:
            REALTIME_DATA.append(payload)
        case PAYLOAD_TYPE.REALTIME_END: return

