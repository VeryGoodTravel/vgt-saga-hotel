# vgt-saga-hotel

Main repository of the Hotel service.

Handles availability of hotels and their rooms,
temporarily books hotels or fully books them depending on the SAGA transaction stage.

Creates hotel information and available rooms with the data provided by the scrapper.

## Repository

This repository contains additional submodules containing shared libraries of the SAGA microservices implementations.

To update those submodules in the local branch run:

    git submodule update --remote --merge

## Configuration

### Environmental variables

- RABBIT_HOST -> Address of the rabbit server.
- RABBIT_VIRT_HOST -> Virtual host of the rabbit server.
- RABBIT_PORT -> Port of the rabbit server.
- RABBIT_USR -> Username to log in with.
- RABBIT_PASSWORD -> User password to log in with.
- RABBIT_REPLIES -> Queue of the replies sent back to the orchestrator.
- RABBIT_HOTEL -> Queue of the requests sent by the orchestrator to the hotel service.
- DB_SERVER -> Database server name to use.
- DB_NAME_HOTEL -> Database name to use for the hotel service
- DB_PASSWORD -> Database password to use for the database server
- DB_USER -> Username to use on db server connection

## Implementation documentation
XML docs of the project available in the repository in the
file [SagaHotelDocumentation.xml](SagaHotelDocumentation.xml)


## Data storage (PostgreSQL)

Follows CQRS.

Database consists of the 3 main tables:
 - HotelDb -> contains information about each hotel defined with the foreign key of room types offered by the hotel,
 - RoomDb -> contains definition of the room types offered by the hotel with their respectful amount available in the hotel,
 - Booking -> Defines booked hotel rooms. Specifies if the booking is temporary with the DateTime of the temporary booking and the time the booking takes place. Is assigned to a hotel and room type.

![Database schema](DB_HOTEL.png)
