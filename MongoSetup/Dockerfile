# https://flowygo.com/en/blog/mongodb-and-docker-how-to-create-and-configure-a-replica-set/

FROM mongo:latest

RUN mkdir /config
WORKDIR /config

COPY mongo-setup.js .

CMD [ "bash", "-c", "sleep 10 && mongo mongodb://mongodb1:27017 mongo-setup.js"]
