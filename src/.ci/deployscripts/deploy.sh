echo 'signalrtc release deploy script starting...'
pwd

echo "AGENT_RELEASEDIRECTORY=$AGENT_RELEASEDIRECTORY"
echo "BUILD_DEFINITIONNAME=$BUILD_DEFINITIONNAME"
echo "RELEASE_PRIMARYARTIFACTSOURCEALIAS=$RELEASE_PRIMARYARTIFACTSOURCEALIAS"

sudo systemctl stop signalrtc
sudo cp -r "$RELEASE_PRIMARYARTIFACTSOURCEALIAS"/drop/* /opt/signalrtc
sudo systemctl start signalrtc
echo 'signalrtc release deploy script finished.'
