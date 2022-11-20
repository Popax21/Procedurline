#!/bin/bash
set -e

#Check usage
if [ $# -lt 1 ] 
then
    echo "Usage: $0 <version> [changelog]"
    exit
fi

#Create the zip file
./createZip.sh

if [ $# -lt 2 ]
then
    #Let user enter changelog
    changelog="$(mktemp).md"
    echo "Enter changelog for version $1 here" > $changelog
    xdg-open $changelog
    echo "Enter changelog (your default editor should have opened)"
    read -p "Press enter when done..."
else
    changelog="$2"
fi

#Get confirmation before publishing
echo
echo "Are you sure you want to publish version $1?"
echo "Changelog:"
cat $changelog
echo

select conf in "Yes" "No"; do
    case $conf in
        Yes ) break;;
        * ) echo "Aborting..."; exit;;
    esac
done

#Add changelog to CHANGELOG.txt
changelog2="$(mktemp)"
echo "---------- CHANGELOG VERSION $1 ----------" >> $changelog2
cat $changelog >> $changelog2
echo >> $changelog2
echo >> $changelog2
cat CHANGELOG.txt >> $changelog2
mv -f $changelog2 CHANGELOG.txt

#Create release on GitHub
git commit -m "Automated update to version $1" CHANGELOG.txt
commit="$(git log -n 1 --pretty=format:%H main)"
git tag -a -m "Version $1" v$1 $commit
git push origin main v$1
gh release create v$1 -F $changelog --target $commit Procedurline.zip