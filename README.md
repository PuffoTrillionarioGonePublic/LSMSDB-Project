### Mongo
- export with: `docker run --rm --mount type=bind,source="$(pwd)",target=/export mongo:latest sh -c 'mongodump --db WebsiteDB --host="<ip:port>" --gzip --archive=/export/mongodb.bak.gz'`
- restore with: `docker run --rm --mount type=bind,source="$(pwd)",target=/export mongo:latest sh -c 'mongorestore --host="<ip:port>" --gzip --archive=/export/mongodb.bak.gz'`

### Node4j


### Docker swarm
- first build images: `docker-compose -f <compose-file> build`
- then deploy: `docker stack deploy -c <compose-file> <name>`