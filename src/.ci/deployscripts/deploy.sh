echo 'devcall release deploy script starting...'
pwd

echo "AGENT_RELEASEDIRECTORY=$AGENT_RELEASEDIRECTORY"
echo "BUILD_DEFINITIONNAME=$BUILD_DEFINITIONNAME"
echo "RELEASE_PRIMARYARTIFACTSOURCEALIAS=$RELEASE_PRIMARYARTIFACTSOURCEALIAS"

sudo systemctl stop devcall
sudo cp -r "$RELEASE_PRIMARYARTIFACTSOURCEALIAS"/drop/* /opt/devcall
sudo systemctl start devcall
echo 'devcall release deploy script finished.'
