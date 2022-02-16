To export dabase data (run from Mongodb or Neo4j directory):
`docker run --rm --mount type=bind,source="$(pwd)",target=/backup busybox tar cvfz /backup/backup.tar /backup/data`

To import:
`docker run --rm --mount type=bind,source="$(pwd)",target=/backup busybox sh -c "cd /backup && tar xvf /backup/backup.tar --strip 1"`